namespace NykreditTransactionExporter.WpfSample.Api.Models;

internal sealed class ExportRequest
{
    #region Properties
    public string? Month { get; set; }

    public string? AccountUid { get; set; }

    public string? OutputPath { get; set; }
    #endregion
}

internal sealed class ExportResultModel
{
    #region Properties
    public string OutputPath { get; set; } = string.Empty;

    public string Month { get; set; } = string.Empty;

    public int TransactionCount { get; set; }
    #endregion
}
