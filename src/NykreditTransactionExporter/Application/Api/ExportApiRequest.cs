namespace NykreditTransactionExporter.Application.Api;

internal sealed class ExportApiRequest
{
    #region Properties
    public string? Month { get; set; }

    public string? AccountUid { get; set; }

    public string? OutputPath { get; set; }
    #endregion
}
