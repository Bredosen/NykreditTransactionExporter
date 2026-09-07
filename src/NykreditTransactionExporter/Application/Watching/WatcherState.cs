namespace NykreditTransactionExporter.Application.Watching;

internal sealed class WatcherState
{
    #region Properties
    public int SchemaVersion { get; set; }

    public DateTimeOffset? InitializedAt { get; set; }

    public DateTimeOffset? LastSuccessfulPollAt { get; set; }

    public HashSet<string> InitializedAccountUids { get; set; } = [];

    public HashSet<string> SeenTransactionKeys { get; set; } = [];

    public HashSet<string> WebhookHandledTransactionKeys { get; set; } = [];

    public List<TransactionBookedEvent> RecentEvents { get; set; } = [];
    #endregion
}
