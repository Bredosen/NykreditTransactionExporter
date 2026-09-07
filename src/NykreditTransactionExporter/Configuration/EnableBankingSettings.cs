namespace NykreditTransactionExporter.Configuration;

internal sealed class EnableBankingSettings
{
    #region Properties
    public string ApiBaseUrl { get; set; } = "https://api.enablebanking.com";

    public string ApplicationId { get; set; } = string.Empty;

    public string PrivateKeyPath { get; set; } = string.Empty;

    public string AspspName { get; set; } = "Nykredit Bank";

    public string AspspCountry { get; set; } = "DK";

    public string PsuType { get; set; } = "personal";

    public string RedirectUrl { get; set; } = "https://localhost:53682/callback/";

    public int PreferredConsentDays { get; set; } = 180;
    #endregion
}
