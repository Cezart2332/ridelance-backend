using SharedKernel;

namespace Domain.PfaRegistrations.CompanyFormation;

/// <summary>
/// Dosarul de înființare a societății prin partener (Consulto), pentru șoferii care aleg
/// „Nu am PFA". Ține datele celor trei etape: solicitantul, sediul social și acordul semnat.
///
/// Se leagă de <see cref="PfaRegistration"/>, nu direct de user: dosarul PFA e deja unitatea
/// pe care o văd adminul și contabilul, iar plata înființării atârnă tot de el.
/// </summary>
public sealed class CompanyFormationRequest : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    public CompanyFormationStatus Status { get; set; } = CompanyFormationStatus.Draft;

    /// <summary>Ultima etapă atinsă — pentru reluarea dosarului de unde a rămas.</summary>
    public CompanyFormationStage CurrentStage { get; set; } = CompanyFormationStage.PersonalData;

    // --- Etapa 1: solicitantul ---
    public PersoanaFizica Solicitant { get; set; } = new();

    /// <summary>
    /// Ce a venit din OCR-ul pe CI și ce a editat userul manual, ca JSON
    /// (<c>{"cnp": {"prefilled": true, "manuallyEdited": false}}</c>). Strat de audit pentru
    /// review-ul adminului — sursa de adevăr rămân coloanele de mai sus.
    /// </summary>
    public string? PrefilledFields { get; set; }

    // --- Etapa 2: sediul social ---
    public RegisteredOfficeType? OfficeType { get; set; }

    /// <summary>Adresa aleasă din lista Consulto, când <see cref="OfficeType"/> e ConsultoProvided.</summary>
    public Guid? ConsultoOfficeId { get; set; }

    /// <summary>Solicitantul e proprietarul imobilului. Null cât timp nu s-a ales „adresă proprie".</summary>
    public bool? IsOwner { get; set; }

    public Adresa OfficeAddress { get; set; } = new();

    public bool AcknowledgedOwnershipDocs { get; set; }
    public bool AcknowledgedSubmitLater { get; set; }

    /// <summary>Se cere doar când solicitantul NU e proprietar.</summary>
    public bool? AcknowledgedOwnerConsent { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAtUtc { get; set; }

    /// <summary>Motivul pentru care adminul a cerut corecturi (status InfoRequested).</summary>
    public string? AdminNote { get; set; }

    // Navigation
    public PfaRegistration PfaRegistration { get; set; } = null!;
    public ConsultoOffice? ConsultoOffice { get; set; }
    public List<CompanyFormationOwner> Owners { get; set; } = [];
    public List<CompanyFormationConsent> Consents { get; set; } = [];
    public CompanyFormationSignature? Signature { get; set; }

    /// <summary>Dosarul e trimis — șoferul nu mai poate edita nimic.</summary>
    public bool IsLocked => Status is not (CompanyFormationStatus.Draft or CompanyFormationStatus.InfoRequested);

    /// <summary>Etapa 1 e completă și se poate trece la sediul social.</summary>
    public bool PersonalDataComplete => Solicitant.IsComplete;

    /// <summary>
    /// Etapa 2 e completă și se poate trece la semnare. Pe ramura Consulto e de ajuns adresa
    /// aleasă; pe ramura proprie se cer adresa, bifele și cel puțin un proprietar când imobilul
    /// nu e al solicitantului.
    /// </summary>
    public bool RegisteredOfficeComplete => OfficeType switch
    {
        RegisteredOfficeType.ConsultoProvided => ConsultoOfficeId is not null,
        RegisteredOfficeType.Own =>
            OfficeAddress.IsCompleteForRegisteredOffice
            && IsOwner is not null
            && AcknowledgedOwnershipDocs
            && AcknowledgedSubmitLater
            && (IsOwner == true || AcknowledgedOwnerConsent == true && Owners.Count > 0),
        _ => false,
    };
}
