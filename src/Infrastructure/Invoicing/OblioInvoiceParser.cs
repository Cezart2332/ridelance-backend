using System.Globalization;
using System.Text.Json;
using Application.Abstractions.Services;

namespace Infrastructure.Invoicing;

/// <summary>
/// Traduce un element din <c>docs/invoice/list</c> în <see cref="OwnerInvoice"/>.
/// </summary>
/// <remarks>
/// Stă separat și e public în interiorul asamblării fiindcă e singura parte a integrării care se
/// poate verifica fără să existe un cont Oblio: restul e transport.
///
/// API-ul Oblio întoarce numerele când ca numere, când ca șiruri („1842.27"), iar câmpurile
/// opționale lipsesc cu totul în loc să fie null. Fiecare citire de aici presupune ambele forme.
/// </remarks>
public static class OblioInvoiceParser
{
    /// <summary>`null` pentru o intrare fără serie sau număr — fără ele factura nu e adresabilă.</summary>
    public static OwnerInvoice? Parse(JsonElement item)
    {
        string? seriesName = ReadString(item, "seriesName");
        string? number = ReadString(item, "number");

        if (string.IsNullOrWhiteSpace(seriesName) || string.IsNullOrWhiteSpace(number))
        {
            return null;
        }

        DateOnly? issueDate = ReadDate(item, "issueDate");
        if (issueDate is null)
        {
            return null;
        }

        decimal total = ReadDecimal(item, "total") ?? 0m;

        // Oblio nu are un singur nume pentru suma încasată; ambele apar în răspunsuri.
        decimal collected = ReadDecimal(item, "collected") ?? ReadDecimal(item, "collectedValue") ?? 0m;

        (string clientName, string? clientCif) = ReadClient(item);

        return new OwnerInvoice(
            seriesName!,
            number!,
            issueDate.Value,
            ReadDate(item, "dueDate"),
            clientName,
            clientCif,
            total,
            collected,
            ReadString(item, "link"),
            ReadBool(item, "canceled"));
    }

    /// <summary>Clientul vine ca obiect imbricat sau, la răspunsuri mai vechi, ca șir simplu.</summary>
    private static (string Name, string? Cif) ReadClient(JsonElement item)
    {
        if (!item.TryGetProperty("client", out JsonElement client))
        {
            return ("Client necunoscut", null);
        }

        if (client.ValueKind == JsonValueKind.String)
        {
            return (client.GetString() ?? "Client necunoscut", null);
        }

        if (client.ValueKind != JsonValueKind.Object)
        {
            return ("Client necunoscut", null);
        }

        string name = ReadString(client, "name") ?? "Client necunoscut";
        string? cif = ReadString(client, "cif");
        return (name, string.IsNullOrWhiteSpace(cif) ? null : cif);
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null,
        };
    }

    private static decimal? ReadDecimal(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(
                value.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal parsed) => parsed,
            _ => null,
        };
    }

    private static DateOnly? ReadDate(JsonElement element, string property)
    {
        string? raw = ReadString(element, property);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // ISO întâi, apoi formatul românesc pe care îl întorc unele endpoint-uri.
        string[] formats = ["yyyy-MM-dd", "dd.MM.yyyy", "dd-MM-yyyy"];
        return DateOnly.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date
            : null;
    }

    /// <summary>Anularea vine ca boolean, ca „1"/„0" sau ca text — toate înseamnă același lucru.</summary>
    private static bool ReadBool(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.GetDecimal() != 0,
            JsonValueKind.String => value.GetString() is "1" or "true" or "True",
            _ => false,
        };
    }
}
