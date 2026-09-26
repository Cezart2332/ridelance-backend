using System.Globalization;
using System.Text.Json;

namespace Application.Accounting;

/// <summary>Serializarea coloanelor jsonb ale modulului și formatarea românească din mesaje.</summary>
public static class AccountingJson
{
    /// <summary>Aceeași formă ca API-ul: camelCase, enum-urile prin convertoarele lor.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string? json, T fallback) =>
        string.IsNullOrWhiteSpace(json) ? fallback : JsonSerializer.Deserialize<T>(json, Options) ?? fallback;

    private static readonly NumberFormatInfo Romanian = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberGroupSizes = [3],
    };

    /// <summary><c>1.234,56</c> — ca în frontend (<c>formatAmount</c>).</summary>
    public static string Amount(decimal value) => value.ToString("#,##0.00", Romanian);

    /// <summary><c>dd.MM.yyyy</c>, sau „—” pentru o dată lipsă.</summary>
    public static string Date(DateOnly? value) =>
        value?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "—";
}
