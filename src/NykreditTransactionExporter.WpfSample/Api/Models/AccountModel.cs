namespace NykreditTransactionExporter.WpfSample.Api.Models;

internal sealed class AccountModel
{
    #region Properties
    public string Uid { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Iban { get; set; }

    public string? Currency { get; set; }
    #endregion
}
