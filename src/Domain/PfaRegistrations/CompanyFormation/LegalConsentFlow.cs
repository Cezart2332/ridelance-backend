using SharedKernel;

namespace Domain.PfaRegistrations.CompanyFormation;

/// <summary>
/// Textele juridice ale unui wizard de consimțământ, versionate. Nu se hardcodează în
/// frontend: juridicul le va schimba, iar acordurile deja date trebuie să rămână legate de
/// versiunea afișată atunci. O singură versiune e activă la un moment dat per context.
/// </summary>
public sealed class LegalConsentFlow : Entity
{
    public Guid Id { get; set; }

    /// <summary>Contextul fluxului (ex. „infiintare-societate").</summary>
    public string Context { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Versiunea activă e cea servită clienților. Cele vechi rămân pentru snapshot-uri.</summary>
    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<LegalConsentStep> Steps { get; set; } = [];
}

/// <summary>Un pas din wizard: titlu, declarație și eticheta bifei.</summary>
public sealed class LegalConsentStep : Entity
{
    public Guid Id { get; set; }
    public Guid LegalConsentFlowId { get; set; }

    public int Position { get; set; }

    /// <summary>Cheia stabilă a pasului, salvată în <see cref="CompanyFormationConsent.StepKey"/>.</summary>
    public string Key { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>Paragraful de declarație juridică.</summary>
    public string Body { get; set; } = string.Empty;

    public string CheckboxLabel { get; set; } = string.Empty;

    // Navigation
    public LegalConsentFlow LegalConsentFlow { get; set; } = null!;
}
