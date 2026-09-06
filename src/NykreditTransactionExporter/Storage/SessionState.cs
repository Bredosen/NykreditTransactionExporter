namespace NykreditTransactionExporter.Storage;

internal sealed class SessionState
{
    #region Properties
    public string SessionId { get; set; } = string.Empty;

    public string BankName { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string PsuType { get; set; } = string.Empty;

    public DateTimeOffset? ValidUntil { get; set; }

    public DateTimeOffset SavedAt { get; set; }

    public List<AccountState> Accounts { get; set; } = [];
    #endregion
}
