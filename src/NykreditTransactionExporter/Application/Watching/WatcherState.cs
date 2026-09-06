namespace NykreditTransactionExporter.Application.Watching;

internal sealed class WatcherState
{
    #region Properties
    public DateTimeOffset? InitializedAt { get; set; }

    public DateTimeOffset? LastSuccessfulPollAt { get; set; }

    public HashSet<string> InitializedAccountUids { get; set; } = [];

    public HashSet<string> SeenTransactionKeys { get; set; } = [];
    #endregion
}
