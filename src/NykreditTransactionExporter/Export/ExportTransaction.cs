namespace NykreditTransactionExporter.Export;

internal sealed class ExportTransaction
{
    #region Properties
    public string AccountUid { get; set; } = string.Empty;

    public string Account { get; set; } = string.Empty;

    public string Iban { get; set; } = string.Empty;

    public DateOnly? BookingDate { get; set; }

    public DateOnly? TransactionDate { get; set; }

    public DateOnly? ValueDate { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string Direction { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Counterparty { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;

    public string EntryReference { get; set; } = string.Empty;

    public string TransactionId { get; set; } = string.Empty;

    public decimal? BalanceAfter { get; set; }
    #endregion
}
