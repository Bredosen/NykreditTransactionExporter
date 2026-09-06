namespace NykreditTransactionExporter.Configuration;

internal sealed class WatcherSettings
{
    #region Properties
    public string StateFile { get; set; } = "data/watcher-state.json";

    public string WebhookUrl { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    public int IntervalMinutes { get; set; } = 360;

    public int LookbackDays { get; set; } = 7;

    public bool NotifyExistingOnFirstRun { get; set; }
    #endregion
}
