using System.Globalization;
using System.Text;
using Application.Abstractions.Services;

namespace Application.Accounting.Registers;

/// <summary>
/// Exportul CSV al unui registru (spec registre §3): antetul ca rânduri de o celulă, capul de tabel,
/// rândurile. Separator „;” și virgulă zecimală, cum le deschide Excel în română; UTF-8 cu BOM.
/// </summary>
internal static class RegisterCsv
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public static byte[] Write(RegisterDocument document)
    {
        var csv = new StringBuilder();
        Line(csv, [$"{document.Title} ({document.FormCode})"]);
        foreach (string header in document.Header)
        {
            Line(csv, [header]);
        }

        Line(csv, [.. document.Columns.Select(c => c.Header)]);
        foreach (RegisterLine line in document.Lines)
        {
            Line(csv, [.. line.Cells.Select(Cell)]);
        }

        foreach (string note in document.Notes)
        {
            Line(csv, [note]);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private static string Cell(object? value) => value switch
    {
        null => string.Empty,
        decimal amount => amount.ToString("0.00", Ro),
        IFormattable formattable => formattable.ToString(null, Ro),
        _ => value.ToString() ?? string.Empty,
    };

    private static void Line(StringBuilder csv, IEnumerable<string> cells) =>
        csv.Append(string.Join(';', cells.Select(Quote))).Append("\r\n");

    private static string Quote(string cell) =>
        cell.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{cell.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : cell;
}
