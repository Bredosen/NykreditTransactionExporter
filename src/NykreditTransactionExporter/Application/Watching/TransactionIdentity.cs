using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NykreditTransactionExporter.Export;

namespace NykreditTransactionExporter.Application.Watching;

internal static class TransactionIdentity
{
    #region Create transaction base key
    public static string CreateBaseKey(ExportTransaction transaction)
    {
        string canonicalValue;
        if (!string.IsNullOrWhiteSpace(transaction.EntryReference))
        {
            canonicalValue = string.Join(
                '\u001f',
                transaction.AccountUid,
                transaction.EntryReference.Trim());
        }
        else
        {
            canonicalValue = string.Join(
                '\u001f',
                transaction.AccountUid,
                FormatDate(transaction.BookingDate),
                FormatDate(transaction.TransactionDate),
                FormatDate(transaction.ValueDate),
                transaction.Amount.ToString(CultureInfo.InvariantCulture),
                transaction.Currency,
                transaction.Direction,
                transaction.Description,
                transaction.Counterparty,
                transaction.Reference);
        }

        return Hash(canonicalValue);
    }
    #endregion

    #region Create event id
    public static string CreateEventId(string transactionKey)
    {
        return Hash($"transaction.booked\u001f{transactionKey}");
    }
    #endregion

    #region Format transaction date
    private static string FormatDate(DateOnly? value)
    {
        return value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
    }
    #endregion

    #region Hash canonical identity
    private static string Hash(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
    #endregion
}
