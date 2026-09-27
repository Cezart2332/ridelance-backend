using System.Globalization;

namespace Application.Accounting;

/// <summary>
/// O sumă scrisă ca text → <see cref="decimal"/>. Documentele reale folosesc ambele convenții:
/// Bolt scrie <c>2273.23</c>, Uber <c>2.535,66</c>. Ultimul separator e cel zecimal dacă are după
/// el cel mult două cifre; celelalte sunt separatori de mii. Aceeași regulă ca în frontend
/// (<c>parseAmount</c>).
/// </summary>
public static class AmountText
{
    public static decimal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string compact = new([.. text.Where(c => char.IsDigit(c) || c is '.' or ',' or '-')]);
        if (compact.Length == 0)
        {
            return null;
        }

        int last = Math.Max(compact.LastIndexOf(','), compact.LastIndexOf('.'));
        int decimals = last >= 0 ? compact.Length - last - 1 : 0;
        bool hasDecimals = last >= 0 && decimals is > 0 and <= 2;
        string whole = (hasDecimals ? compact[..last] : compact).Replace(".", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal);
        string normalized = hasDecimals ? $"{whole}.{compact[(last + 1)..]}" : whole;

        return decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : null;
    }
}
