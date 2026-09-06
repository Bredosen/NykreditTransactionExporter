namespace NykreditTransactionExporter.EnableBanking;

internal sealed class BankInfo
{
    #region Properties
    public string Name { get; }

    public string Country { get; }

    public long MaximumConsentValiditySeconds { get; }
    #endregion

    #region Create bank information
    public BankInfo(string name, string country, long maximumConsentValiditySeconds)
    {
        Name = name;
        Country = country;
        MaximumConsentValiditySeconds = maximumConsentValiditySeconds;
    }
    #endregion
}
