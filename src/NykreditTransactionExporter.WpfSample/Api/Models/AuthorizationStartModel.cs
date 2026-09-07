namespace NykreditTransactionExporter.WpfSample.Api.Models;

internal sealed class AuthorizationStartModel
{
    #region Properties
    public string Url { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string RedirectUrl { get; set; } = string.Empty;

    public DateTimeOffset ValidUntil { get; set; }
    #endregion
}
