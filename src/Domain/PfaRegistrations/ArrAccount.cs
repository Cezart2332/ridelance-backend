using SharedKernel;

namespace Domain.PfaRegistrations;

/// <summary>
/// Contul de trezorerie al unei agenții teritoriale ARR — unde se plătește tariful de eliberare
/// a autorizației de transport alternativ.
///
/// Tabel, nu constante în cod: IBAN-urile și codurile fiscale se schimbă fără deploy (o agenție
/// își mută contul, un județ primește altă trezorerie), iar dovada plății din dosar trebuie să
/// arate contul valabil la momentul depunerii, nu cel din build.
/// </summary>
public sealed class ArrAccount : Entity
{
    public Guid Id { get; set; }

    /// <summary>Codul auto al județului: `CT`, `B`, `IF`. Cheia stabilă, nu denumirea.</summary>
    public string CountyCode { get; set; } = string.Empty;

    /// <summary>Denumirea afișată, cu diacritice: „Bistrița-Năsăud”.</summary>
    public string CountyName { get; set; } = string.Empty;

    /// <summary>Trezoreria la care e deschis contul.</summary>
    public string Treasury { get; set; } = string.Empty;

    /// <summary>Codul fiscal al agenției teritoriale.</summary>
    public string FiscalCode { get; set; } = string.Empty;

    /// <summary>IBAN-ul, stocat fără spații. Gruparea în blocuri de 4 e treaba UI-ului.</summary>
    public string Iban { get; set; } = string.Empty;

    /// <summary>Un cont dezactivat nu se mai propune, dar rămâne pentru dosarele vechi.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Beneficiarul plății, exact cum se scrie pe ordinul de plată.</summary>
    public string BeneficiaryName => $"A.R.R. — Agenția Teritorială {CountyName}";
}
