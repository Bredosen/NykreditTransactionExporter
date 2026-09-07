namespace NykreditTransactionExporter.Application.Operations;

internal sealed class AccountInfo
{
    #region Properties
    public string Uid { get; }

    public string DisplayName { get; }

    public string? Iban { get; }

    public string? Currency { get; }
    #endregion

    #region Create account information
    public AccountInfo(string uid, string displayName, string? iban, string? currency)
    {
        Uid = uid;
        DisplayName = displayName;
        Iban = iban;
        Currency = currency;
    }
    #endregion
}
