using Domain.Users;
using SharedKernel;

namespace Domain.Documents;

public sealed class Document : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? PfaRegistrationId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DocumentCategory Category { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;
    public string EncryptedFilePath { get; set; } = string.Empty;
    public string EncryptionIv { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; set; }
    public DocumentAiStatus AiStatus { get; set; } = DocumentAiStatus.None;
    public string? AiSummary { get; set; }
    public string? AiDetectedType { get; set; }
    public DateTime? AiExtractedExpiresAtUtc { get; set; }
    public DateTime? AiProcessedAtUtc { get; set; }
    public int AiAttempts { get; set; }

    // OCR extins (Faza 0 — nefolosite până la pipeline-ul extins)
    /// <summary>Încrederea globală auto-raportată de model (0..1).</summary>
    public double? AiConfidence { get; set; }

    /// <summary>JSON-ul câmpurilor extrase (redactat — fără CNP / serie-număr act în clar).</summary>
    public string? AiExtractedJson { get; set; }

    /// <summary>Documentul are câmpuri sub prag și așteaptă verificare manuală (nu blochează fluxul).</summary>
    public bool AiRequiresManualReview { get; set; }

    // Legături/atribute adăugate progresiv de pașii noi de onboarding
    /// <summary>Vehiculul clientului de care ține documentul (fără FK încă — se adaugă la Pasul 5).</summary>
    public Guid? PfaVehicleId { get; set; }

    /// <summary>Platforma de care ține documentul (Uber/Bolt) când e cazul.</summary>
    public string? PlatformProvider { get; set; }

    /// <summary>Data emiterii documentului (pentru autorizații/contracte generate).</summary>
    public DateTime? IssuedAtUtc { get; set; }

    /// <summary>Când un document e reîncărcat/regenerat, cel vechi pointează spre înlocuitor.</summary>
    public Guid? ReplacedByDocumentId { get; set; }

    // Navigation
    public User User { get; set; } = null!;
}
