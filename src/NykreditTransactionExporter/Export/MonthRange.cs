using System.Globalization;

namespace NykreditTransactionExporter.Export;

internal sealed class MonthRange
{
    #region Properties
    public DateOnly Start { get; }

    public DateOnly End { get; }

    public string Label { get; }
    #endregion

    #region Create month range
    private MonthRange(DateOnly start, DateOnly end, string label)
    {
        Start = start;
        End = end;
        Label = label;
    }
    #endregion

    #region Resolve requested month
    public static MonthRange Create(string? requestedMonth)
    {
        DateOnly firstDay;
        if (string.IsNullOrWhiteSpace(requestedMonth))
        {
            DateOnly today = DateOnly.FromDateTime(DateTime.Today);
            firstDay = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        }
        else
        {
            string value = requestedMonth.Trim();
            if (!DateOnly.TryParseExact(
                    $"{value}-01",
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out firstDay))
            {
                throw new ArgumentException("--month must use yyyy-MM, for example 2026-08.");
            }
        }

        DateOnly end = firstDay.AddMonths(1).AddDays(-1);
        return new MonthRange(firstDay, end, firstDay.ToString("yyyy-MM", CultureInfo.InvariantCulture));
    }
    #endregion
}
