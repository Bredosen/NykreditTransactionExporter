namespace NykreditTransactionExporter.Storage;

internal sealed class AccountState
{
    #region Properties
    public string Uid { get; set; } = string.Empty;

    public string? Iban { get; set; }

    public string? Name { get; set; }

    public string? Product { get; set; }

    public string? Details { get; set; }

    public string? Currency { get; set; }
    #endregion
}
