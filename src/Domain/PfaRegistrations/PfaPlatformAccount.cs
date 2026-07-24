using SharedKernel;

namespace Domain.PfaRegistrations;

public sealed class PfaPlatformAccount : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public PfaPlatformProvider Provider { get; set; }
    public PfaPlatformAccountKind Kind { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? FullName { get; set; }
    public PfaFleetAccountStatus Status { get; set; } = PfaFleetAccountStatus.NotConfigured;

    // Pasul 4 — onboarding conturi operator (Uber/Bolt). Fără parole.
    /// <summary>Userul a selectat această platformă în onboarding.</summary>
    public bool IsSelectedByUser { get; set; }
    /// <summary>Userul are deja cont de operator pe platformă.</summary>
    public bool HasExistingAccount { get; set; }
    /// <summary>Identificatorul contului de operator (nu parolă).</summary>
    public string? OperatorAccountId { get; set; }
    /// <summary>Contractul de afiliere semnat cu platforma.</summary>
    public Guid? AffiliationContractDocumentId { get; set; }
    public PfaPlatformOnboardingStatus OnboardingStatus { get; set; } = PfaPlatformOnboardingStatus.NotStarted;
    public DateTime? ConfiguredAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedByUserId { get; set; }

    public PfaRegistration PfaRegistration { get; set; } = null!;
}
