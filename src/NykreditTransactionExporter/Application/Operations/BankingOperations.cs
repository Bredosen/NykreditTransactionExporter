using System.Globalization;
using System.Text.Json;
using NykreditTransactionExporter.Configuration;
using NykreditTransactionExporter.EnableBanking;
using NykreditTransactionExporter.EnableBanking.Data;
using NykreditTransactionExporter.Export;
using NykreditTransactionExporter.Storage;

namespace NykreditTransactionExporter.Application.Operations;

internal sealed class BankingOperations
{
    #region Readonly Fields
    private readonly AppSettings _settings;
    private readonly EnableBankingClient _client;
    private readonly SessionStore _sessionStore;
    private readonly CsvExporter _csvExporter;
    #endregion

    #region Create banking operations
    public BankingOperations(
        AppSettings settings,
        EnableBankingClient client,
        SessionStore sessionStore,
        CsvExporter csvExporter)
    {
        _settings = settings;
        _client = client;
        _sessionStore = sessionStore;
        _csvExporter = csvExporter;
    }
    #endregion

    #region Start bank authorization
    public async Task<AuthorizationChallenge> StartAuthorizationAsync(CancellationToken cancellationToken)
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

        return new AuthorizationChallenge(
            authorization.Url,
            state,
            bankSettings.RedirectUrl,
            validUntil);
    }
    #endregion

    #region Complete bank authorization
    public async Task<SessionInfo> CompleteAuthorizationAsync(
        string code,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("An authorization code is required.", nameof(code));
        }

        SessionState session = await _client.AuthorizeSessionAsync(code.Trim(), cancellationToken);
        await _sessionStore.SaveAsync(session, cancellationToken);
        return CreateSessionInfo(session, "AUTHORIZED", session.ValidUntil);
    }
    #endregion

    #region Get current session information
    public async Task<SessionInfo> GetSessionInfoAsync(CancellationToken cancellationToken)
    {
        SessionState session = await _sessionStore.LoadAsync(cancellationToken);
        using JsonDocument document = await _client.GetSessionAsync(session.SessionId, cancellationToken);
        JsonElement root = document.RootElement;
        string status = root.TryGetProperty("status", out JsonElement statusValue)
            ? statusValue.GetString() ?? "unknown"
            : "unknown";
        DateTimeOffset? validUntil = root.TryGetProperty("access", out JsonElement access) &&
                                     access.TryGetProperty("valid_until", out JsonElement validUntilValue)
            ? ParseDateTimeOffset(validUntilValue.GetString()) ?? session.ValidUntil
            : session.ValidUntil;

        return CreateSessionInfo(session, status, validUntil);
    }
    #endregion

    #region Get current session information when optional
    public async Task<SessionInfo> GetSessionInfoOrEmptyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await GetSessionInfoAsync(cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return SessionInfo.CreateEmpty();
        }
    }
    #endregion

    #region Get raw application data
    public async Task<JsonElement> GetApplicationDataAsync(CancellationToken cancellationToken)
    {
        using JsonDocument document = await _client.GetApplicationAsync(cancellationToken);
        return document.RootElement.Clone();
    }
    #endregion

    #region Get raw ASPSP data
    public async Task<JsonElement> GetAspspsDataAsync(
        string? country,
        string? psuType,
        string? service,
        string? paymentType,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = await _client.GetAspspsAsync(
            country,
            psuType,
            service,
            paymentType,
            cancellationToken);
        return document.RootElement.Clone();
    }
    #endregion

    #region Get raw session data
    public async Task<JsonElement> GetSessionDataAsync(CancellationToken cancellationToken)
    {
        SessionState session = await _sessionStore.LoadAsync(cancellationToken);
        using JsonDocument document = await _client.GetSessionAsync(session.SessionId, cancellationToken);
        return document.RootElement.Clone();
    }
    #endregion

    #region Get saved authorization data
    public async Task<JsonElement> GetAuthorizationDataAsync(CancellationToken cancellationToken)
    {
        SessionState session = await _sessionStore.LoadAsync(cancellationToken);
        if (!session.AuthorizationData.HasValue ||
            session.AuthorizationData.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new InvalidOperationException(
                "The saved session does not contain the original authorization response. Re-authorize once to capture it.");
        }

        return session.AuthorizationData.Value.Clone();
    }
    #endregion

    #region Get raw account details
    public async Task<JsonElement> GetAccountDetailsDataAsync(
        string accountUid,
        PsuRequestHeaders? psuHeaders,
        CancellationToken cancellationToken)
    {
        await LoadAuthorizedSessionForAccountAsync(accountUid, "retrieving account details", cancellationToken);
        using JsonDocument document = await _client.GetAccountDetailsAsync(
            accountUid,
            psuHeaders,
            cancellationToken);
        return document.RootElement.Clone();
    }
    #endregion

    #region Get raw account balances
    public async Task<JsonElement> GetAccountBalancesDataAsync(
        string accountUid,
        PsuRequestHeaders? psuHeaders,
        CancellationToken cancellationToken)
    {
        await LoadAuthorizedSessionForAccountAsync(accountUid, "retrieving account balances", cancellationToken);
        using JsonDocument document = await _client.GetAccountBalancesAsync(
            accountUid,
            psuHeaders,
            cancellationToken);
        return document.RootElement.Clone();
    }
    #endregion

    #region Get raw transaction page
    public async Task<JsonElement> GetAccountTransactionsPageDataAsync(
        string accountUid,
        TransactionQuery query,
        PsuRequestHeaders? psuHeaders,
        CancellationToken cancellationToken)
    {
        await LoadAuthorizedSessionForAccountAsync(accountUid, "retrieving account transactions", cancellationToken);
        using JsonDocument document = await _client.GetTransactionsPageAsync(
            accountUid,
            query,
            psuHeaders,
            cancellationToken);
        return document.RootElement.Clone();
    }
    #endregion

    #region Get all raw transactions
    public async Task<IReadOnlyList<JsonElement>> GetAllAccountTransactionsDataAsync(
        string accountUid,
        TransactionQuery query,
        PsuRequestHeaders? psuHeaders,
        CancellationToken cancellationToken)
    {
        await LoadAuthorizedSessionForAccountAsync(accountUid, "retrieving account transactions", cancellationToken);
        return await _client.GetAllTransactionsAsync(accountUid, query, psuHeaders, cancellationToken);
    }
    #endregion

    #region Get raw transaction details
    public async Task<JsonElement> GetTransactionDetailsDataAsync(
        string accountUid,
        string transactionId,
        PsuRequestHeaders? psuHeaders,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            throw new ArgumentException("transactionId is required.", nameof(transactionId));
        }

        await LoadAuthorizedSessionForAccountAsync(accountUid, "retrieving transaction details", cancellationToken);
        using JsonDocument document = await _client.GetTransactionDetailsAsync(
            accountUid,
            transactionId.Trim(),
            psuHeaders,
            cancellationToken);
        return document.RootElement.Clone();
    }
    #endregion

    #region Export monthly transactions
    public async Task<ExportResult> ExportAsync(
        string? requestedMonth,
        string? requestedAccountUid,
        string? requestedOutputPath,
        CancellationToken cancellationToken)
    {
        MonthRange month = MonthRange.Create(requestedMonth);
        SessionState session = await _sessionStore.LoadAsync(cancellationToken);
        await EnsureAuthorizedSessionAsync(session, "exporting", cancellationToken);

        IReadOnlyList<AccountState> accounts = SelectAccounts(session, requestedAccountUid);
        var exportRows = new List<ExportTransaction>();
        var accountResults = new List<AccountExportResult>();
        foreach (AccountState account in accounts)
        {
            IReadOnlyList<JsonElement> transactions = await _client.GetBookedTransactionsAsync(
                account.Uid,
                month.Start,
                month.End,
                cancellationToken);

            exportRows.AddRange(transactions.Select(transaction => TransactionMapper.Map(account, transaction)));
            accountResults.Add(new AccountExportResult(
                account.Uid,
                AccountDisplayName(account),
                transactions.Count));
        }

        ExportTransaction[] orderedRows = exportRows
            .OrderBy(row => row.BookingDate ?? row.TransactionDate ?? DateOnly.MinValue)
            .ThenBy(row => row.EntryReference, StringComparer.Ordinal)
            .ToArray();

        string outputPath = await _csvExporter.ExportAsync(
            orderedRows,
            month,
            requestedOutputPath,
            cancellationToken);

        return new ExportResult(outputPath, month.Label, orderedRows.Length, accountResults);
    }
    #endregion

    #region Ensure session is authorized
    private async Task EnsureAuthorizedSessionAsync(
        SessionState session,
        string operation,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = await _client.GetSessionAsync(session.SessionId, cancellationToken);
        string? status = document.RootElement.TryGetProperty("status", out JsonElement statusValue)
            ? statusValue.GetString()
            : null;

        if (!string.Equals(status, "AUTHORIZED", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The saved Enable Banking session is '{status ?? "unknown"}'. Run authorize again before {operation}.");
        }
    }
    #endregion

    #region Load authorized account session
    private async Task<SessionState> LoadAuthorizedSessionForAccountAsync(
        string accountUid,
        string operation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accountUid))
        {
            throw new ArgumentException("accountUid is required.", nameof(accountUid));
        }

        SessionState session = await _sessionStore.LoadAsync(cancellationToken);
        await EnsureAuthorizedSessionAsync(session, operation, cancellationToken);
        SelectAccounts(session, accountUid.Trim());
        return session;
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
            ? [account]
            : throw new ArgumentException($"Account uid '{requestedUid}' does not exist in the saved session.");
    }
    #endregion

    #region Create session information
    private static SessionInfo CreateSessionInfo(
        SessionState session,
        string status,
        DateTimeOffset? validUntil)
    {
        AccountInfo[] accounts = session.Accounts
            .Select(MapAccount)
            .ToArray();
        return new SessionInfo(true, status, validUntil, session.SavedAt, accounts);
    }
    #endregion

    #region Map account information
    private static AccountInfo MapAccount(AccountState account)
    {
        return new AccountInfo(
            account.Uid,
            AccountDisplayName(account),
            account.Iban,
            account.Currency);
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

    #region Parse date and time
    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset parsed)
            ? parsed
            : null;
    }
    #endregion
}
