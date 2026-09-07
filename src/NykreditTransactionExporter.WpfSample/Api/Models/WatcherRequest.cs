namespace NykreditTransactionExporter.WpfSample.Api.Models;

internal sealed class WatcherRequest
{
    #region Properties
    public string? AccountUid { get; set; }
    #endregion
}

internal sealed class WatcherStatusModel
{
    #region Properties
    public bool Running { get; set; }

    public string? AccountUid { get; set; }

    public DateTimeOffset? LastSuccessfulPollAt { get; set; }

    public string? LastError { get; set; }
    #endregion
}
