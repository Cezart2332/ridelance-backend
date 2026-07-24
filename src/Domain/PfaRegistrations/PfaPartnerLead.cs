using SharedKernel;

namespace Domain.PfaRegistrations;

/// <summary>
/// Pasul 1, ramura „Nu am PFA": lead trimis către partenerul de înființare (Consulto).
/// Flux ghidat în aplicație (întrebări, consimțământ, status), mutat manual din admin —
/// fără API extern încă. Nu se cer parole prin formular.
/// </summary>
public sealed class PfaPartnerLead : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    public string Provider { get; set; } = "Consulto";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? County { get; set; }

    /// <summary>Cum e găzduit sediul social (ex. proprietate, chirie, comodat).</summary>
    public string? HousingType { get; set; }

    public bool DataSharingConsent { get; set; }
    public DateTime? DataSharingConsentAtUtc { get; set; }

    public PfaPartnerLeadStatus Status { get; set; } = PfaPartnerLeadStatus.RequestSent;
    public string? AdminNote { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public PfaRegistration PfaRegistration { get; set; } = null!;
}
