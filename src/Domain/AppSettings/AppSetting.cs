using SharedKernel;

namespace Domain.AppSettings;

/// <summary>
/// Parametru comercial/operațional configurabil fără deploy (taxe ARR, preț Oblio,
/// preț copie conformă, ecusoane etc.). Valorile se snapshot-uiesc pe cerere la
/// generarea dosarului, ca o modificare ulterioară să nu rescrie istoricul.
/// </summary>
public sealed class AppSetting : Entity
{
    public Guid Id { get; set; }

    /// <summary>Cheie stabilă (ex. "fees.arr.authorization", "oblio.yearly.eur").</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Valoarea serializată JSON (număr, string sau obiect).</summary>
    public string ValueJson { get; set; } = "null";

    public string? Description { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedByUserId { get; set; }
}
