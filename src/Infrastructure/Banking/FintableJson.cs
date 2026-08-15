using System.Globalization;
using System.Text.Json;

namespace Infrastructure.Banking;

/// <summary>
/// Citire tolerantă a răspunsurilor Fintable.
///
/// OpenAPI-ul declară `Account`, `Transaction` și `Connection` ca obiecte generice, fără
/// proprietăți — numele de câmpuri le știm doar din exemplele din documentație. Deci fiecare
/// câmp se caută sub mai multe denumiri plauzibile și lipsa lui nu e o eroare, ci un null.
/// Alternativa — să presupunem o formă exactă — ar transforma orice schimbare de la ei într-o
/// excepție la runtime.
/// </summary>
internal static class FintableJson
{
    public static string? String(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
            {
                continue;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    string? text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }

                    break;

                // Unele câmpuri (nume de instituție) pot veni ca obiect {name: ...}.
                case JsonValueKind.Object:
                    string? nested = String(value, "name", "display_name", "title");
                    if (nested is not null)
                    {
                        return nested;
                    }

                    break;

                case JsonValueKind.Number:
                    return value.ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// Sumele vin ca string (`"5240.12"`, `"-4.50"`), cu punct zecimal. Se citesc invariant —
    /// cu cultura română, „1.234" ar deveni 1234 în loc de 1,234, adică o mie de lei diferență.
    /// </summary>
    public static decimal? Decimal(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                string? text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text) &&
                    decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    public static DateOnly? Date(JsonElement element, params string[] names)
    {
        string? text = String(element, names);
        if (text is null)
        {
            return null;
        }

        if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            return date;
        }

        // Unele câmpuri sunt timestamp complet; ne interesează doar ziua.
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset moment)
            ? DateOnly.FromDateTime(moment.UtcDateTime)
            : null;
    }

    public static DateTime? Timestamp(JsonElement element, params string[] names)
    {
        string? text = String(element, names);
        return text is not null &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset moment)
            ? moment.UtcDateTime
            : null;
    }

    /// <summary>Despachetează plicul `{data: ...}` folosit de toate răspunsurile.</summary>
    public static JsonElement Data(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out JsonElement data)
            ? data
            : root;

    public static string? Cursor(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object ? String(root, "next_cursor") : null;
}
