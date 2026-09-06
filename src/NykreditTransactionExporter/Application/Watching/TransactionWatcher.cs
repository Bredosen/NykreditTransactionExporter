using System.Text.Json;
using NykreditTransactionExporter.Configuration;
using NykreditTransactionExporter.EnableBanking;
using NykreditTransactionExporter.Export;
using NykreditTransactionExporter.Storage;

namespace NykreditTransactionExporter.Application.Watching;

internal sealed class TransactionWatcher
{
    #region Fields
    private readonly WatcherSettings _settings;
    private readonly EnableBankingClient _client;
    private readonly SessionStore _sessionStore;
    private readonly WatcherStateStore _stateStore;
    private readonly WebhookNotifier _webhookNotifier;
    #endregion

    #region Create transaction watcher
    public TransactionWatcher(
        WatcherSettings settings,
        EnableBankingClient client,
        SessionStore sessionStore,
        WatcherStateStore stateStore,
        WebhookNotifier webhookNotifier)
    {
        _settings = settings;
        _client = client;
        _sessionStore = sessionStore;
        _stateStore = stateStore;
        _webhookNotifier = webhookNotifier;
    }
    #endregion

    #region Run transaction watcher
    public async Task RunAsync(
        string? requestedAccountUid,
        bool runOnce,
        CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        using FileStream watcherLock = AcquireWatcherLock();

        SessionState session = await _sessionStore.LoadAsync(cancellationToken);
        await EnsureAuthorizedSessionAsync(session, cancellationToken);
        IReadOnlyList<AccountState> accounts = SelectAccounts(session, requestedAccountUid);

        if (runOnce)
        {
            await PollAsync(accounts, true, cancellationToken);
            return;
        }

        TimeSpan interval = TimeSpan.FromMinutes(_settings.IntervalMinutes);
        Console.WriteLine(
            $"Watching {accounts.Count} account(s) every {_settings.IntervalMinutes} minute(s). Press Ctrl+C to stop.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await PollAsync(accounts, false, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"Watcher poll failed: {exception.Message}");
                }

                await Task.Delay(interval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine("Watcher stopped.");
        }
    }
    #endregion

    #region Poll booked transactions
    private async Task PollAsync(
        IReadOnlyList<AccountState> accounts,
        bool failOnDeliveryError,
        CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly dateFrom = today.AddDays(-(_settings.LookbackDays - 1));
        var transactions = new List<ExportTransaction>();

        foreach (AccountState account in accounts)
        {
            IReadOnlyList<JsonElement> fetched = await _client.GetBookedTransactionsAsync(
                account.Uid,
                dateFrom,
                today,
                cancellationToken);
            transactions.AddRange(fetched.Select(transaction => TransactionMapper.Map(account, transaction)));
        }

        IReadOnlyList<(string Key, ExportTransaction Transaction)> observations = BuildObservations(transactions);
        WatcherState state = await _stateStore.LoadAsync(cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        state.InitializedAt ??= now;

        string[] baselineAccounts = accounts
            .Select(account => account.Uid)
            .Where(uid => !state.InitializedAccountUids.Contains(uid))
            .ToArray();
        if (baselineAccounts.Length > 0)
        {
            foreach (string accountUid in baselineAccounts)
            {
                state.InitializedAccountUids.Add(accountUid);
            }

            if (!_settings.NotifyExistingOnFirstRun)
            {
                var baselineAccountSet = new HashSet<string>(baselineAccounts, StringComparer.OrdinalIgnoreCase);
                foreach (var (key, transaction) in observations)
                {
                    if (baselineAccountSet.Contains(transaction.AccountUid))
                    {
                        state.SeenTransactionKeys.Add(key);
                    }
                }

                int baselineTransactions = observations.Count(item =>
                    baselineAccountSet.Contains(item.Transaction.AccountUid));
                Console.WriteLine(
                    $"Watcher baseline created for {baselineAccounts.Length} account(s) with " +
                    $"{baselineTransactions} existing booked transaction(s); no baseline webhooks were sent.");
            }

            await _stateStore.SaveAsync(state, cancellationToken);
        }

        int delivered = 0;
        int failures = 0;
        foreach (var (key, transaction) in observations)
        {
            if (state.SeenTransactionKeys.Contains(key))
            {
                continue;
            }

            try
            {
                await _webhookNotifier.SendBookedTransactionAsync(key, transaction, cancellationToken);
                state.SeenTransactionKeys.Add(key);
                await _stateStore.SaveAsync(state, cancellationToken);
                delivered++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine(
                    $"Webhook delivery failed for {TransactionLabel(transaction)}: {exception.Message}");
            }
        }

        state.LastSuccessfulPollAt = now;
        await _stateStore.SaveAsync(state, cancellationToken);
        Console.WriteLine(
            $"Watcher poll {dateFrom:yyyy-MM-dd}..{today:yyyy-MM-dd}: " +
            $"{observations.Count} booked, {delivered} new delivered, {failures} failed.");

        if (failOnDeliveryError && failures > 0)
        {
            throw new InvalidOperationException(
                $"{failures} webhook delivery attempt(s) failed. Failed transactions remain unacknowledged and will be retried.");
        }
    }
    #endregion

    #region Build stable observations
    private static IReadOnlyList<(string Key, ExportTransaction Transaction)> BuildObservations(
        IEnumerable<ExportTransaction> transactions)
    {
        var observations = new List<(string Key, ExportTransaction Transaction)>();
        IEnumerable<IGrouping<string, ExportTransaction>> groups = transactions
            .GroupBy(TransactionIdentity.CreateBaseKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (IGrouping<string, ExportTransaction> group in groups)
        {
            int occurrence = 0;
            foreach (ExportTransaction transaction in group
                         .OrderBy(item => item.TransactionId, StringComparer.Ordinal)
                         .ThenBy(item => item.Description, StringComparer.Ordinal))
            {
                occurrence++;
                observations.Add(($"{group.Key}:{occurrence}", transaction));
            }
        }

        return observations
            .OrderBy(item => item.Transaction.BookingDate ?? item.Transaction.TransactionDate ?? DateOnly.MinValue)
            .ThenBy(item => item.Transaction.AccountUid, StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
    }
    #endregion

    #region Ensure session is authorized
    private async Task EnsureAuthorizedSessionAsync(
        SessionState session,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = await _client.GetSessionAsync(session.SessionId, cancellationToken);
        string? status = document.RootElement.TryGetProperty("status", out JsonElement statusValue)
            ? statusValue.GetString()
            : null;

        if (!string.Equals(status, "AUTHORIZED", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The saved Enable Banking session is '{status ?? "unknown"}'. Run authorize again before watching.");
        }
    }
    #endregion

    #region Select accounts
    private static IReadOnlyList<AccountState> SelectAccounts(SessionState session, string? requestedUid)
    {
        if (string.IsNullOrWhiteSpace(requestedUid))
        {
            return session.Accounts;
        }

        AccountState? account = session.Accounts.FirstOrDefault(candidate =>
            string.Equals(candidate.Uid, requestedUid, StringComparison.OrdinalIgnoreCase));
        return account is not null
            ? new[] { account }
            : throw new ArgumentException($"Account uid '{requestedUid}' does not exist in the saved session.");
    }
    #endregion

    #region Acquire watcher lock
    private FileStream AcquireWatcherLock()
    {
        string lockPath = $"{_settings.StateFile}.lock";
        string? directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                "Another watcher instance is already using the configured Watcher.StateFile.",
                exception);
        }
    }
    #endregion

    #region Validate watcher configuration
    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_settings.WebhookUrl))
        {
            throw new InvalidOperationException(
                "Watcher.WebhookUrl is required for the watch command. Configure it in appsettings.json or NYKREDIT_WEBHOOK_URL.");
        }
    }
    #endregion

    #region Format transaction label
    private static string TransactionLabel(ExportTransaction transaction)
    {
        DateOnly? date = transaction.BookingDate ?? transaction.TransactionDate;
        string dateText = date?.ToString("yyyy-MM-dd") ?? "unknown-date";
        return $"{transaction.AccountUid}/{dateText}/{transaction.Amount} {transaction.Currency}";
    }
    #endregion
}
