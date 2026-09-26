namespace OpenCsms.Application.Billing;

using System.Globalization;

/// <summary>
/// A calendar month on the export surface, parsed from the <c>YYYY-MM</c> the endpoint carries.
/// The range is UTC: an invoice belongs to the month its <c>IssuedAtUtc</c> falls in, from the
/// month's first instant up to (but excluding) the next month's.
/// </summary>
public sealed record InvoiceMonth(int Year, int Month)
{
    /// <summary>The month's first instant, UTC.</summary>
    public DateTimeOffset Start => new(Year, Month, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The first instant past the month, UTC; the range's exclusive end.</summary>
    public DateTimeOffset ExclusiveEnd => Start.AddMonths(1);

    /// <summary>Whether the instant falls inside the month.</summary>
    public bool Contains(DateTimeOffset instant) => instant >= Start && instant < ExclusiveEnd;

    /// <summary>Parses <c>YYYY-MM</c>; anything else is the caller's validation failure.</summary>
    public static InvoiceMonth Parse(string? month)
    {
        if (!string.IsNullOrWhiteSpace(month)
            && month.Length == 7
            && month[4] == '-'
            && int.TryParse(month.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            && int.TryParse(month.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var calendarMonth)
            && calendarMonth is >= 1 and <= 12)
        {
            return new InvoiceMonth(year, calendarMonth);
        }

        throw new ArgumentException("A month is 'YYYY-MM', for example '2030-05'.", nameof(month));
    }

    public override string ToString() => $"{Year:D4}-{Month:D2}";
}
