using System.Globalization;
using System.Text;

namespace NykreditTransactionExporter.Export;

internal sealed class CsvExporter
{
    #region Fields
    private static readonly CultureInfo DanishCulture = CultureInfo.GetCultureInfo("da-DK");
    private readonly string _exportDirectory;
    #endregion

    #region Create CSV exporter
    public CsvExporter(string exportDirectory)
    {
        _exportDirectory = exportDirectory;
    }
    #endregion

    #region Export transactions
    public async Task<string> ExportAsync(
        IReadOnlyCollection<ExportTransaction> transactions,
        MonthRange month,
        string? requestedOutputPath,
        CancellationToken cancellationToken)
    {
        string outputPath = ResolveOutputPath(month, requestedOutputPath);
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(true));
        await writer.WriteLineAsync(string.Join(';', new[]
        {
            "AccountUid",
            "Account",
            "IBAN",
            "BookingDate",
            "TransactionDate",
            "ValueDate",
            "Amount",
            "Currency",
            "Direction",
            "Status",
            "Description",
            "Counterparty",
            "Reference",
            "EntryReference",
            "TransactionId",
            "BalanceAfter"
        }.Select(Escape)));

        foreach (ExportTransaction transaction in transactions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] values =
            {
                transaction.AccountUid,
                transaction.Account,
                transaction.Iban,
                FormatDate(transaction.BookingDate),
                FormatDate(transaction.TransactionDate),
                FormatDate(transaction.ValueDate),
                transaction.Amount.ToString("0.00", DanishCulture),
                transaction.Currency,
                transaction.Direction,
                transaction.Status,
                transaction.Description,
                transaction.Counterparty,
                transaction.Reference,
                transaction.EntryReference,
                transaction.TransactionId,
                transaction.BalanceAfter?.ToString("0.00", DanishCulture) ?? string.Empty
            };

            await writer.WriteLineAsync(string.Join(';', values.Select(Escape)));
        }

        return outputPath;
    }
    #endregion

    #region Resolve output path
    private string ResolveOutputPath(MonthRange month, string? requestedOutputPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedOutputPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedOutputPath));
        }

        return Path.Combine(_exportDirectory, $"Posteringer-{month.Label}.csv");
    }
    #endregion

    #region Format date
    private static string FormatDate(DateOnly? value)
    {
        return value?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
    }
    #endregion

    #region Escape CSV value
    private static string Escape(string? value)
    {
        string escaped = (value ?? string.Empty).Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
    #endregion
}
