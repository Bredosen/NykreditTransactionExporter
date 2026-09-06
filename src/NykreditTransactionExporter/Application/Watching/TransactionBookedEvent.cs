using NykreditTransactionExporter.Export;

namespace NykreditTransactionExporter.Application.Watching;

internal sealed class TransactionBookedEvent
{
    #region Properties
    public string EventId { get; }

    public string Type { get; } = "transaction.booked";

    public DateTimeOffset OccurredAt { get; }

    public ExportTransaction Transaction { get; }
    #endregion

    #region Create booked transaction event
    public TransactionBookedEvent(
        string eventId,
        DateTimeOffset occurredAt,
        ExportTransaction transaction)
    {
        EventId = eventId;
        OccurredAt = occurredAt;
        Transaction = transaction;
    }
    #endregion
}
