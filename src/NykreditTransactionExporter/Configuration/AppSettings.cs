namespace NykreditTransactionExporter.Configuration;

internal sealed class AppSettings
{
    #region Properties
    public EnableBankingSettings EnableBanking { get; set; } = new();

    public StorageSettings Storage { get; set; } = new();

    public WatcherSettings Watcher { get; set; } = new();
    #endregion
}
