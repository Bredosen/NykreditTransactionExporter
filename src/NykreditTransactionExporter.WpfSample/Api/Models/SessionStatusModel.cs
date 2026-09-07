namespace NykreditTransactionExporter.WpfSample.Api.Models;

internal sealed class SessionStatusModel
{
    #region Properties
    public bool HasSession { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset? ValidUntil { get; set; }

    public DateTimeOffset? SavedAt { get; set; }

    public List<AccountModel> Accounts { get; set; } = [];
    #endregion
}
