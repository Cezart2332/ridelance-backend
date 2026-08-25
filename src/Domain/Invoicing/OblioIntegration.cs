using SharedKernel;

namespace Domain.Invoicing;

/// <summary>
/// Contul Oblio al unui proprietar, folosit ca să emită facturi pe CIF-ul lui.
/// </summary>
/// <remarks>
/// Distinct de <c>OblioOptions</c> din configurare, care e contul RIDElance și emite facturile
/// platformei către clienții ei. Aici e invers: fiecare PFA sau SRL își facturează propriii
/// clienți, deci are nevoie de credențialele lui.
///
/// Secretul se stochează criptat, ca la <c>BoltIntegration</c>, și nu pleacă niciodată către
/// frontend — se confirmă doar că există.
/// </remarks>
public sealed class OblioIntegration : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Emailul contului Oblio — `client_id` în OAuth-ul lor.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Token-ul din Oblio → Setări → Date cont, criptat la stocare.</summary>
    public string ClientSecretEncrypted { get; set; } = string.Empty;

    /// <summary>CIF-ul pe care se emit facturile. Trebuie să existe în contul Oblio.</summary>
    public string Cif { get; set; } = string.Empty;

    /// <summary>Seria implicită, aleasă dintre cele existente în contul lui.</summary>
    public string? SeriesName { get; set; }

    /// <summary>Denumirea citită din Oblio la conectare — confirmă vizual că e contul corect.</summary>
    public string? CompanyName { get; set; }

    public bool IsConnected { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Ultima aducere a facturilor din Oblio.</summary>
    public DateTime? LastSyncAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
