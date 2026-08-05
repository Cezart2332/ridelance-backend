using Domain.Documents;
using Domain.Users;
using SharedKernel;

namespace Domain.PfaRegistrations;

public sealed class PfaRegistration : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public RegistrationType RegistrationType { get; set; }
    public PfaRegistrationStatus Status { get; set; } = PfaRegistrationStatus.Pending;

    // "Am PFA" fields
    public string? FullName { get; set; }
    public string? Phone { get; set; }
    public string? Cui { get; set; }

    // Date PFA completate la validarea dosarului (Pasul 1)
    public PfaSource PfaSource { get; set; } = PfaSource.Existing;
    public string? LegalName { get; set; }
    public string? RegistryNumber { get; set; }
    /// <summary>Coduri CAEN (JSON array), completate la validarea dosarului PFA.</summary>
    public string? CaenCodes { get; set; }

    // --- Citite din actele de la ONRC, pentru ramura „Am PFA" ---

    /// <summary>Titularul PFA-ului, așa cum apare pe certificatul de înregistrare.</summary>
    public string? HolderName { get; set; }

    /// <summary>Sediul profesional, ca text — așa cum apare în acte. Nu se procesează structurat.</summary>
    public string? ProfessionalOffice { get; set; }

    /// <summary>Activitățile autorizate din certificatul constatator.</summary>
    public string? AuthorizedActivities { get; set; }

    /// <summary>Unde se desfășoară activitatea: la sediu, la terți sau ambele.</summary>
    public string? ActivityLocation { get; set; }

    /// <summary>Punctele de lucru declarate, dacă există.</summary>
    public string? WorkPoints { get; set; }

    // "Nu am PFA" fields
    public int? ContractDuration { get; set; }
    public string? Street { get; set; }
    public string? Number { get; set; }
    public string? City { get; set; }
    public string? County { get; set; }
    public bool IsOwner { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }

    /// <summary>
    /// Momentul înrolării reale — se setează DOAR când toate secțiunile obligatorii de
    /// onboarding sunt validate (nu la aprobarea dosarului PFA). Singura sursă de adevăr
    /// pentru „PFA înrolat". Vezi <see cref="OnboardingProgress"/>.
    /// </summary>
    public DateTime? OnboardingCompletedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public Guid? AssignedContabilId { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public User? ReviewedByUser { get; set; }
    public User? AssignedContabil { get; set; }
    public PfaFiscalProfile? FiscalProfile { get; set; }
    public PfaFleetConsent? FleetConsent { get; set; }
    public PfaPartnerLead? PartnerLead { get; set; }
    public CompanyFormation.CompanyFormationRequest? CompanyFormationRequest { get; set; }
    public OnboardingSignaturePacket? SignaturePacket { get; set; }
    public PfaBankAccountDeclaration? BankAccountDeclaration { get; set; }
    public PfaOblioAccount? OblioAccount { get; set; }
    public ArrAuthorizationRequest? ArrAuthorizationRequest { get; set; }
    public List<PfaVehicle> Vehicles { get; set; } = [];
    public List<PfaPlatformAccount> PlatformAccounts { get; set; } = [];
    public List<Document> Documents { get; set; } = [];
    public List<OnboardingSectionApproval> OnboardingSections { get; set; } = [];
}
