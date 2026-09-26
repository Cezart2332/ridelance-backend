using Domain.Documents;
using Domain.PfaRegistrations;
using SharedKernel;

namespace Domain.Accounting;

/// <summary>
/// O factură de comision sau un raport lunar Uber/Bolt, încărcat pentru un PFA și o lună
/// (spec contabilitate B0, B1).
/// </summary>
/// <remarks>
/// Fișierul stă în mecanismul existent de documente (<see cref="Document"/>, criptat), și e
/// imutabil: o corectură înseamnă o <see cref="DocumentExtraction"/> nouă, nu alt fișier.
/// </remarks>
public sealed class PlatformDocument : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    /// <summary>Luna fiscală, <c>yyyy-MM</c>.</summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>Necunoscută până la extracție.</summary>
    public Platform? Platform { get; set; }
    public PlatformDocumentType DocumentType { get; set; } = PlatformDocumentType.Unknown;

    /// <summary>Fișierul original, în tabelul de documente.</summary>
    public Guid SourceDocumentId { get; set; }

    /// <summary>SHA-256 al fișierului, hex. Același hash la același PFA înseamnă duplicat (409).</summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>Statusul stocat. <c>Locked</c> se setează când intră într-o declarație dincolo de Generated sau într-o perioadă închisă.</summary>
    public PlatformDocumentStatus Status { get; set; } = PlatformDocumentStatus.Uploaded;

    public string? ExtractionError { get; set; }

    public Guid UploadedByUserId { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }

    public PfaRegistration PfaRegistration { get; set; } = null!;
    public Document SourceDocument { get; set; } = null!;
    public ICollection<DocumentExtraction> Extractions { get; } = [];
}

/// <summary>
/// O citire a documentului: extracția AI sau o editare manuală. Fiecare creează o versiune nouă;
/// cea curentă e marcată cu <see cref="IsCurrent"/> (spec contabilitate B0).
/// </summary>
public sealed class DocumentExtraction : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PlatformDocumentId { get; set; }
    public int Version { get; set; }
    public bool IsCurrent { get; set; }

    // Câmpurile tipate.
    public string? SupplierName { get; set; }
    public string? SupplierCountry { get; set; }
    public string? SupplierVatId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public DateOnly? PeriodFrom { get; set; }
    public DateOnly? PeriodTo { get; set; }
    public string? Currency { get; set; }

    /// <summary>Totalul documentului; la rapoarte, venitul brut din curse.</summary>
    public decimal? Amount { get; set; }
    public decimal? CommissionAmount { get; set; }

    /// <summary>JSON: <c>[{ label, amount }]</c>.</summary>
    public string OtherAmountsJson { get; set; } = "[]";

    /// <summary>JSON: textul exact din PDF pentru fiecare câmp, <c>{ câmp: fragment }</c>.</summary>
    public string SourceSnippetsJson { get; set; } = "{}";

    /// <summary>JSON: rezultatul verificărilor deterministe (B1), <c>[{ code, passed, message }]</c>.</summary>
    public string ChecksResultJson { get; set; } = "[]";

    public double? ModelConfidence { get; set; }
    public string? ModelId { get; set; }
    public string? PromptVersion { get; set; }

    public bool IsManualEdit { get; set; }

    /// <summary>JSON: câmpurile modificate manual față de extracția AI, cumulat pe versiuni.</summary>
    public string ManuallyEditedFieldsJson { get; set; } = "[]";

    /// <summary>Motivul obligatoriu al unei editări manuale.</summary>
    public string? EditReason { get; set; }

    /// <summary><c>null</c> pentru extracția automată.</summary>
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public PlatformDocument PlatformDocument { get; set; } = null!;
}
