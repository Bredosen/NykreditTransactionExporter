using System.Globalization;
using System.Text.Json;
using NykreditTransactionExporter.WpfSample.Formatting;

namespace NykreditTransactionExporter.WpfSample.Views.Transactions;

internal sealed class TransactionListItem
{
    #region Properties
    public string Date { get; private init; } = string.Empty;

    public decimal Amount { get; private init; }

    public string Currency { get; private init; } = string.Empty;

    public string Description { get; private init; } = string.Empty;

    public string Counterparty { get; private init; } = string.Empty;

    public string Status { get; private init; } = string.Empty;

    public string Direction { get; private init; } = string.Empty;

    public string TransactionId { get; private init; } = string.Empty;

    public string EntryReference { get; private init; } = string.Empty;

    public string RawJson { get; private init; } = string.Empty;
    #endregion

    #region Create item from API transaction
    public static TransactionListItem FromJson(JsonElement transaction)
    {
        string indicator = GetString(transaction, "credit_debit_indicator") ?? string.Empty;
        decimal amount = GetAmount(transaction, "transaction_amount") ?? 0m;
        if (string.Equals(indicator, "DBIT", StringComparison.OrdinalIgnoreCase))
        {
            amount = -Math.Abs(amount);
        }
        else if (string.Equals(indicator, "CRDT", StringComparison.OrdinalIgnoreCase))
        {
            amount = Math.Abs(amount);
        }

        string debtor = GetNestedString(transaction, "debtor", "name") ?? string.Empty;
        string creditor = GetNestedString(transaction, "creditor", "name") ?? string.Empty;
        string counterparty = string.Equals(indicator, "DBIT", StringComparison.OrdinalIgnoreCase)
            ? FirstNonEmpty(creditor, debtor)
            : FirstNonEmpty(debtor, creditor);
        string description = JoinNonEmpty(
            " | ",
            JoinStringArray(transaction, "remittance_information"),
            GetNestedString(transaction, "bank_transaction_code", "description"),
            GetString(transaction, "note"));

        return new TransactionListItem
        {
            Date = FirstNonEmpty(
                GetString(transaction, "booking_date"),
                GetString(transaction, "transaction_date"),
                GetString(transaction, "value_date")),
            Amount = amount,
            Currency = GetNestedString(transaction, "transaction_amount", "currency") ?? string.Empty,
            Description = string.IsNullOrWhiteSpace(description) ? "(no description)" : description,
            Counterparty = counterparty,
            Status = GetString(transaction, "status") ?? string.Empty,
            Direction = string.Equals(indicator, "DBIT", StringComparison.OrdinalIgnoreCase) ? "Debit" : "Credit",
            TransactionId = GetString(transaction, "transaction_id") ?? string.Empty,
            EntryReference = GetString(transaction, "entry_reference") ?? string.Empty,
            RawJson = JsonFormatting.Format(transaction)
        };
    }
    #endregion

    #region Read amount
    private static decimal? GetAmount(JsonElement element, string propertyName)
    {
        string? value = GetNestedString(element, propertyName, "amount");
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed
            : null;
    }
    #endregion

    #region Read string
    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
    #endregion

    #region Read nested string
    private static string? GetNestedString(JsonElement element, string objectName, string propertyName)
    {
        return element.TryGetProperty(objectName, out JsonElement nested) &&
               nested.ValueKind == JsonValueKind.Object
            ? GetString(nested, propertyName)
            : null;
    }
    #endregion

    #region Join string array
    private static string JoinStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        IEnumerable<string> values = array
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim());
        return string.Join(" | ", values);
    }
    #endregion

    #region Select first non-empty value
    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
    #endregion

    #region Join non-empty values
    private static string JoinNonEmpty(string separator, params string?[] values)
    {
        return string.Join(
            separator,
            values.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .Distinct());
    }
    #endregion
}
