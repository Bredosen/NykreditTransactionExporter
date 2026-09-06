namespace NykreditTransactionExporter.Configuration;

internal sealed class StorageSettings
{
    #region Properties
    public string SessionFile { get; set; } = "data/session.json";

    public string ExportDirectory { get; set; } = "exports";
    #endregion
}
