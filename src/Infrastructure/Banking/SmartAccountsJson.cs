using System.Globalization;
using System.Text.Json;

namespace Infrastructure.Banking;

/// <summary>
/// Citire tolerantă a răspunsurilor Smart Accounts.
///
/// Furnizorul normalizează cele cincisprezece bănci sub aceleași scheme, dar normalizarea nu e
/// perfectă: câmpuri declarate în OpenAPI lipsesc la unele bănci (IBAN la conturile de card,
/// `valueDate` la altele), iar sumele vin când ca număr, când ca string. Deci fiecare câmp se
/// caută sub mai multe denumiri plauzibile, iar lipsa lui e un null, nu o excepție.
/// </summary>
internal static class SmartAccountsJson
{
    public static string? String(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

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

                // Contrapartida vine uneori ca obiect: {iban, currency, name}.
                case JsonValueKind.Object:
                    string? nested = String(value, "name", "iban");
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
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

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

    public static bool? Bool(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (string name in names)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
            {
                continue;
            }

            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
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

    /// <summary>
    /// Despachetează plicul `{status, messageStatus, payload}` în care vine absolut orice răspuns.
    /// </summary>
    public static JsonElement Payload(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("payload", out JsonElement payload)
            ? payload
            : root;

    /// <summary>
    /// Pagina următoare de tranzacții: `_links.next.href` sau `links.next.href`, după bancă.
    /// </summary>
    public static string? NextPage(JsonElement payload)
    {
        foreach (string container in new[] { "_links", "links" })
        {
            if (payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty(container, out JsonElement links) &&
                links.ValueKind == JsonValueKind.Object &&
                links.TryGetProperty("next", out JsonElement next))
            {
                string? href = next.ValueKind == JsonValueKind.String ? next.GetString() : String(next, "href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    return href;
                }
            }
        }

        return null;
    }
}
