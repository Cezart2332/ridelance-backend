using SharedKernel;

namespace Domain.PfaRegistrations.CompanyFormation;

/// <summary>
/// Semnătura electronică simplă aplicată la finalul wizardului, cu probatoriul de audit.
/// Tot ce ține de context (IP, dispozitiv, moment) se completează pe server — un client
/// nu-și poate proba singur propria semnătură.
/// </summary>
public sealed class CompanyFormationSignature : Entity
{
    public Guid Id { get; set; }
    public Guid CompanyFormationRequestId { get; set; }

    /// <summary>Documentul cu imaginea semnăturii (PNG transparent), stocat criptat ca orice document.</summary>
    public Guid? ImageDocumentId { get; set; }

    /// <summary>Traseele semnăturii ca JSON, pentru re-randare la orice rezoluție în actele PDF.</summary>
    public string? VectorData { get; set; }

    public int CanvasWidth { get; set; }
    public int CanvasHeight { get; set; }

    // --- Probatoriu, completat exclusiv de server ---
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceType { get; set; }
    public string? Os { get; set; }
    public string? Browser { get; set; }

    /// <summary>Ceasul serverului, nu al clientului.</summary>
    public DateTime SignedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// SHA-256 peste imaginea semnăturii plus întreg payload-ul de onboarding (identitate,
    /// sediu, consimțăminte). Dovada că semnătura aparține <em>acestui</em> set exact de date:
    /// dacă datele se schimbă, hash-ul nu mai corespunde și semnătura se invalidează.
    /// </summary>
    public string PayloadHash { get; set; } = string.Empty;

    /// <summary>Cheia de idempotență a cererii care a creat semnătura.</summary>
    public string? IdempotencyKey { get; set; }

    // Navigation
    public CompanyFormationRequest CompanyFormationRequest { get; set; } = null!;
}
