namespace NykreditTransactionExporter.Application.Operations;

internal sealed class ExportResult
{
    #region Properties
    public string OutputPath { get; }

    public string Month { get; }

    public int TransactionCount { get; }

    public IReadOnlyList<AccountExportResult> Accounts { get; }
    #endregion

    #region Create export result
    public ExportResult(
        string outputPath,
        string month,
        int transactionCount,
        IReadOnlyList<AccountExportResult> accounts)
    {
        OutputPath = outputPath;
        Month = month;
        TransactionCount = transactionCount;
        Accounts = accounts;
    }
    #endregion
}

internal sealed class AccountExportResult
{
    #region Properties
    public string Uid { get; }

    public string DisplayName { get; }

    public int TransactionCount { get; }
    #endregion

    #region Create account export result
    public AccountExportResult(string uid, string displayName, int transactionCount)
    {
        Uid = uid;
        DisplayName = displayName;
        TransactionCount = transactionCount;
    }
    #endregion
}
