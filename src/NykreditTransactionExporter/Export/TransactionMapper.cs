using System.Globalization;
using System.Text.Json;
using NykreditTransactionExporter.Storage;

namespace NykreditTransactionExporter.Export;

internal static class TransactionMapper
{
    #region Map API transaction
    public static ExportTransaction Map(AccountState account, JsonElement transaction)
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

        string remittance = JoinStringArray(transaction, "remittance_information");
        string bankDescription = GetNestedString(transaction, "bank_transaction_code", "description") ?? string.Empty;
        string note = GetString(transaction, "note") ?? string.Empty;
        string description = JoinNonEmpty(" | ", remittance, bankDescription, note);

        string debtor = GetNestedString(transaction, "debtor", "name") ?? string.Empty;
        string creditor = GetNestedString(transaction, "creditor", "name") ?? string.Empty;
        string counterparty = string.Equals(indicator, "DBIT", StringComparison.OrdinalIgnoreCase)
            ? FirstNonEmpty(creditor, debtor)
            : FirstNonEmpty(debtor, creditor);

        return new ExportTransaction
        {
            AccountUid = account.Uid,
            Account = FirstNonEmpty(account.Product, account.Details, account.Name, account.Uid),
            Iban = account.Iban ?? string.Empty,
            BookingDate = GetDate(transaction, "booking_date"),
            TransactionDate = GetDate(transaction, "transaction_date"),
            ValueDate = GetDate(transaction, "value_date"),
            Amount = amount,
            Currency = GetNestedString(transaction, "transaction_amount", "currency") ?? account.Currency ?? string.Empty,
            Direction = string.Equals(indicator, "DBIT", StringComparison.OrdinalIgnoreCase) ? "Debit" : "Credit",
            Status = GetString(transaction, "status") ?? string.Empty,
            Description = description,
            Counterparty = counterparty,
            Reference = GetString(transaction, "reference_number") ?? string.Empty,
            EntryReference = GetString(transaction, "entry_reference") ?? string.Empty,
            TransactionId = GetString(transaction, "transaction_id") ?? string.Empty,
            BalanceAfter = GetAmount(transaction, "balance_after_transaction")
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

    #region Read date
    private static DateOnly? GetDate(JsonElement element, string propertyName)
    {
        string? value = GetString(element, propertyName);
        return DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly parsed)
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
            values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).Distinct());
    }
    #endregion
}
