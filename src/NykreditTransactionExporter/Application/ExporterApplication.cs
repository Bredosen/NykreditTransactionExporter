using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using NykreditTransactionExporter.Application.Watching;
using NykreditTransactionExporter.Configuration;
using NykreditTransactionExporter.EnableBanking;
using NykreditTransactionExporter.Export;
using NykreditTransactionExporter.Storage;

namespace NykreditTransactionExporter.Application;

internal sealed class ExporterApplication
{
    #region Fields
    private readonly AppSettings _settings;
    private readonly EnableBankingClient _client;
    private readonly SessionStore _sessionStore;
    private readonly CsvExporter _csvExporter;
    private readonly CallbackReceiver _callbackReceiver;
    private readonly TransactionWatcher _transactionWatcher;
    #endregion

    #region Create application
    public ExporterApplication(
        AppSettings settings,
        EnableBankingClient client,
        SessionStore sessionStore,
        CsvExporter csvExporter,
        CallbackReceiver callbackReceiver,
        TransactionWatcher transactionWatcher)
    {
        _settings = settings;
        _client = client;
        _sessionStore = sessionStore;
        _csvExporter = csvExporter;
        _callbackReceiver = callbackReceiver;
        _transactionWatcher = transactionWatcher;
    }
    #endregion

    #region Run command
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        CommandLineOptions options = CommandLineOptions.Parse(args);
        switch (options.Command)
        {
            case "authorize":
                await AuthorizeAsync(cancellationToken);
                return 0;
            case "status":
                await ShowStatusAsync(cancellationToken);
                return 0;
            case "export":
                await ExportAsync(options, cancellationToken);
                return 0;
            case "watch":
                await _transactionWatcher.RunAsync(options.AccountUid, options.Once, cancellationToken);
                return 0;
            case "help":
                ShowHelp();
                return 0;
            default:
                throw new ArgumentException($"Unknown command: {options.Command}");
        }
    }
    #endregion

    #region Authorize bank access
    private async Task AuthorizeAsync(CancellationToken cancellationToken)
    {
        EnableBankingSettings bankSettings = _settings.EnableBanking;
        BankInfo bank = await _client.GetBankAsync(
            bankSettings.AspspName,
            bankSettings.AspspCountry,
            bankSettings.PsuType,
            cancellationToken);

        TimeSpan bankMaximum = TimeSpan.FromSeconds(bank.MaximumConsentValiditySeconds);
        TimeSpan preferred = TimeSpan.FromDays(bankSettings.PreferredConsentDays);
        TimeSpan consentDuration = preferred <= bankMaximum ? preferred : bankMaximum;
        if (consentDuration > TimeSpan.FromMinutes(2))
        {
            consentDuration -= TimeSpan.FromMinutes(1);
        }

        DateTimeOffset validUntil = DateTimeOffset.UtcNow.Add(consentDuration);
        string state = Guid.NewGuid().ToString("D");
        AuthorizationStart authorization = await _client.StartAuthorizationAsync(
            bankSettings,
            bank,
            state,
            validUntil,
            cancellationToken);

        var redirectUri = new Uri(bankSettings.RedirectUrl, UriKind.Absolute);
        Console.WriteLine("Open this URL to authorize access with Nykredit/MitID:");
        Console.WriteLine(authorization.Url);
        TryOpenBrowser(authorization.Url);

        string code;
        if (_callbackReceiver.CanListen(redirectUri))
        {
            try
            {
                code = await _callbackReceiver.ListenAsync(redirectUri, state, cancellationToken);
            }
            catch (HttpListenerException exception)
            {
                Console.WriteLine($"Local callback listener could not start: {exception.Message}");
                code = _callbackReceiver.ReadFromConsole(state);
            }
        }
        else
        {
            code = _callbackReceiver.ReadFromConsole(state);
        }

        SessionState session = await _client.AuthorizeSessionAsync(code, cancellationToken);
        await _sessionStore.SaveAsync(session, cancellationToken);

        Console.WriteLine($"Session saved. Accessible accounts: {session.Accounts.Count}.");
        if (session.ValidUntil.HasValue)
        {
            Console.WriteLine($"Session valid until: {session.ValidUntil.Value:O}");
        }

        foreach (AccountState account in session.Accounts)
        {
            Console.WriteLine($"- {AccountDisplayName(account)} [{account.Uid}]");
        }
    }
    #endregion

    #region Show session status
    private async Task ShowStatusAsync(CancellationToken cancellationToken)
    {
        SessionState session = await _sessionStore.LoadAsync(cancellationToken);
        using JsonDocument document = await _client.GetSessionAsync(session.SessionId, cancellationToken);
        JsonElement root = document.RootElement;
        string status = root.TryGetProperty("status", out JsonElement statusValue)
            ? statusValue.GetString() ?? "unknown"
            : "unknown";
        string validUntil = root.TryGetProperty("access", out JsonElement access) &&
                            access.TryGetProperty("valid_until", out JsonElement validUntilValue)
            ? validUntilValue.GetString() ?? "unknown"
            : "unknown";

        Console.WriteLine($"Status: {status}");
        Console.WriteLine($"Valid until: {validUntil}");
        Console.WriteLine($"Saved accounts: {session.Accounts.Count}");
    }
    #endregion

    #region Export monthly transactions
    private async Task ExportAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        MonthRange month = MonthRange.Create(options.Month);
        SessionState session = await _sessionStore.LoadAsync(cancellationToken);
        await EnsureAuthorizedSessionAsync(session, cancellationToken);

        IReadOnlyList<AccountState> accounts = SelectAccounts(session, options.AccountUid);
        var exportRows = new List<ExportTransaction>();
        foreach (AccountState account in accounts)
        {
            IReadOnlyList<JsonElement> transactions = await _client.GetBookedTransactionsAsync(
                account.Uid,
                month.Start,
                month.End,
                cancellationToken);

            exportRows.AddRange(transactions.Select(transaction => TransactionMapper.Map(account, transaction)));
            Console.WriteLine($"{AccountDisplayName(account)}: {transactions.Count} booked transactions.");
        }

        ExportTransaction[] orderedRows = exportRows
            .OrderBy(row => row.BookingDate ?? row.TransactionDate ?? DateOnly.MinValue)
            .ThenBy(row => row.EntryReference, StringComparer.Ordinal)
            .ToArray();

        string outputPath = await _csvExporter.ExportAsync(
            orderedRows,
            month,
            options.OutputPath,
            cancellationToken);
        Console.WriteLine($"Exported {orderedRows.Length} transactions to {outputPath}");
    }
    #endregion

    #region Ensure session is authorized
    private async Task EnsureAuthorizedSessionAsync(SessionState session, CancellationToken cancellationToken)
    {
        using JsonDocument document = await _client.GetSessionAsync(session.SessionId, cancellationToken);
        string? status = document.RootElement.TryGetProperty("status", out JsonElement statusValue)
            ? statusValue.GetString()
            : null;

        if (!string.Equals(status, "AUTHORIZED", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The saved Enable Banking session is '{status ?? "unknown"}'. Run authorize again before exporting.");
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

    #region Open authorization URL
    private static void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            Console.WriteLine("The browser could not be opened automatically. Open the URL above manually.");
        }
    }
    #endregion

    #region Format account name
    private static string AccountDisplayName(AccountState account)
    {
        return new[] { account.Product, account.Details, account.Iban, account.Name, account.Uid }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? account.Uid;
    }
    #endregion

    #region Show command help
    private static void ShowHelp()
    {
        Console.WriteLine("NykreditTransactionExporter (.NET 10)");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  authorize");
        Console.WriteLine("      Create or renew the Nykredit account-information session using MitID.");
        Console.WriteLine();
        Console.WriteLine("  status");
        Console.WriteLine("      Show the current Enable Banking session status.");
        Console.WriteLine();
        Console.WriteLine("  export [--month yyyy-MM] [--account uid] [--output path]");
        Console.WriteLine("      Export booked transactions. The previous calendar month is the default.");
        Console.WriteLine();
        Console.WriteLine("  watch [--account uid] [--once]");
        Console.WriteLine("      Poll for newly booked transactions and POST transaction.booked webhooks.");
        Console.WriteLine("      --once performs one poll; otherwise the configured interval is used continuously.");
    }
    #endregion
}
