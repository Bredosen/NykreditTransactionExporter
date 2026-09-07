namespace NykreditTransactionExporter.Application.Operations;

internal sealed class SessionInfo
{
    #region Properties
    public bool HasSession { get; }

    public string Status { get; }

    public DateTimeOffset? ValidUntil { get; }

    public DateTimeOffset? SavedAt { get; }

    public IReadOnlyList<AccountInfo> Accounts { get; }
    #endregion

    #region Create session information
    public SessionInfo(
        bool hasSession,
        string status,
        DateTimeOffset? validUntil,
        DateTimeOffset? savedAt,
        IReadOnlyList<AccountInfo> accounts)
    {
        HasSession = hasSession;
        Status = status;
        ValidUntil = validUntil;
        SavedAt = savedAt;
        Accounts = accounts;
    }
    #endregion

    #region Create empty session information
    public static SessionInfo CreateEmpty()
    {
        return new SessionInfo(false, "NOT_AUTHORIZED", null, null, []);
    }
    #endregion
}
