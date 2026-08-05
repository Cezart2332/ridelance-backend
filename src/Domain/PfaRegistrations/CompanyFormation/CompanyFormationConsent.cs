using SharedKernel;

namespace Domain.PfaRegistrations.CompanyFormation;

/// <summary>
/// Un acord dat în wizardul de consimțământ. Se salvează cu textul integral afișat atunci,
/// nu doar cu versiunea: juridicul va schimba textele, iar acordurile deja date trebuie să
/// rămână legate de ce a citit efectiv omul.
/// </summary>
public sealed class CompanyFormationConsent : Entity
{
    public Guid Id { get; set; }
    public Guid CompanyFormationRequestId { get; set; }

    /// <summary>Cheia pasului din fluxul juridic (ex. „mandat_completare").</summary>
    public string StepKey { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    /// <summary>Textul integral al declarației, exact cum a fost afișat.</summary>
    public string TextSnapshot { get; set; } = string.Empty;

    /// <summary>Eticheta bifei, tot ca snapshot — face parte din ce a acceptat omul.</summary>
    public string CheckboxLabelSnapshot { get; set; } = string.Empty;

    public DateTime AcceptedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public CompanyFormationRequest CompanyFormationRequest { get; set; } = null!;
}
