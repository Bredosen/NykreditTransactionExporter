namespace NykreditTransactionExporter.WpfSample.Api.Models.Watcher;

internal sealed class WatcherEventModel
{
    #region Properties
    public string EventId { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public WatcherTransactionModel Transaction { get; set; } = new();
    #endregion
}

internal sealed class WatcherTransactionModel
{
    #region Properties
    public string AccountUid { get; set; } = string.Empty;

    public string Account { get; set; } = string.Empty;

    public DateOnly? BookingDate { get; set; }

    public DateOnly? TransactionDate { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Counterparty { get; set; } = string.Empty;
    #endregion
}
