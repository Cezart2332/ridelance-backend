using SharedKernel;

namespace Domain.PfaRegistrations;

/// <summary>Starea cererii de autorizație de transport alternativ la ARR (Pasul 3). Avans manual.</summary>
public enum ArrAuthorizationStatus
{
    Draft = 0,
    /// <summary>Dosarul PDF a fost generat și e gata de depus.</summary>
    DossierGenerated = 1,
    /// <summary>Clientul a marcat „Am depus dosarul" la ARR.</summary>
    Submitted = 2,
    /// <summary>Autorizația a fost emisă și încărcată.</summary>
    Issued = 3,
    Rejected = 4,
}

/// <summary>Metoda de depunere a dosarului la ARR.</summary>
public enum ArrSubmissionMethod
{
    /// <summary>Clientul depune personal la agenția ARR.</summary>
    InPersonByClient = 0,
    /// <summary>Depunere online de către RIDElance (respinsă de validator până la confirmarea procedurii).</summary>
    OnlineByRidelance = 1,
}

/// <summary>
/// Pasul 3 — cererea de autorizație de transport alternativ la ARR. Ține ciclul de viață:
/// generarea dosarului PDF, „am depus dosarul", autorizația emisă și datele ei. Cele 4 documente
/// necesare sunt uploadurile existente (certificat, atestat, cazier, adeverință, dovadă plată ARR).
/// </summary>
public sealed class ArrAuthorizationRequest : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    public string? AgencyName { get; set; }

    /// <summary>Taxa ARR, snapshot-uită la generarea dosarului (nu se rescrie la modificări ulterioare).</summary>
    public long FeeSnapshotBani { get; set; }

    public ArrSubmissionMethod SubmissionMethod { get; set; } = ArrSubmissionMethod.InPersonByClient;
    public ArrAuthorizationStatus Status { get; set; } = ArrAuthorizationStatus.Draft;

    // Dosarul generat
    public Guid? DossierDocumentId { get; set; }
    public DateTime? DossierGeneratedAtUtc { get; set; }

    // „Am depus dosarul"
    public DateTime? SubmittedAtUtc { get; set; }

    // Autorizația emisă
    public Guid? AuthorizationDocumentId { get; set; }
    public string? AuthorizationNumber { get; set; }
    public DateOnly? AuthorizationIssuedOn { get; set; }
    public DateOnly? AuthorizationExpiresOn { get; set; }

    public string? AdminNote { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public PfaRegistration PfaRegistration { get; set; } = null!;
}
