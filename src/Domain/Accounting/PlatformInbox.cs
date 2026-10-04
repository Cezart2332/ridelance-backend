using System.Text.Json.Serialization;
using SharedKernel;

namespace Domain.Accounting;

/// <summary>Unde a ajuns un document încărcat global (fără client ales).</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<PlatformInboxStatus>))]
public enum PlatformInboxStatus
{
    /// <summary>Fără CUI în text sau în numele fișierului: se citește, apoi se caută după comision.</summary>
    Matching = 0,

    /// <summary>Alocat unui client: a devenit document de platformă al lui.</summary>
    Assigned = 1,

    /// <summary>Nicio potrivire sigură: Adminul alege clientul.</summary>
    NeedsReview = 2,

    /// <summary>Are un CUI valid care nu e al niciunui client PFA.</summary>
    UnknownCui = 3,

    /// <summary>Respins de Admin (nu ține de niciun client).</summary>
    Dismissed = 4,

    /// <summary>Nu s-a putut citi.</summary>
    Failed = 5,
}

/// <summary>Cum s-a găsit clientul.</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<PlatformInboxMatch>))]
public enum PlatformInboxMatch
{
    Cui = 0,
    FileName = 1,
    Commission = 2,
    Manual = 3,

    /// <summary>Numele clientului citit din document.</summary>
    Name = 4,
}

/// <summary>
/// Un document încărcat din „Clienți PFA” fără client ales: se alocă automat după CUI-ul din text, după
/// numele fișierului sau după comision (raportul Bolt fără CUI ↔ factura de comision Bolt). Ce nu se
/// poate aloca rămâne aici, la vedere, până îl alocă sau îl respinge Adminul.
/// </summary>
public sealed class PlatformInboxItem : Entity, IAccountingRecord
{
    public Guid Id { get; set; }

    /// <summary>Fișierul criptat (<c>Document</c>); devine al clientului la alocare.</summary>
    public Guid SourceDocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;

    /// <summary>Luna aleasă la încărcare (sau citită din numele fișierului), <c>yyyy-MM</c>.</summary>
    public string Period { get; set; } = string.Empty;
    public PlatformInboxStatus Status { get; set; }
    public PlatformInboxMatch? MatchedBy { get; set; }

    /// <summary>De ce e aici sau cum s-a alocat, pe scurt.</summary>
    public string? Reason { get; set; }

    /// <summary>CUI-ul găsit în document, când nu e al niciunui client.</summary>
    public string? DetectedCui { get; set; }
    public Platform? Platform { get; set; }
    public PlatformDocumentType DocumentType { get; set; } = PlatformDocumentType.Unknown;
    public decimal? CommissionAmount { get; set; }
    public DateOnly? PeriodFrom { get; set; }
    public DateOnly? PeriodTo { get; set; }
    public Guid? PfaRegistrationId { get; set; }
    public Guid? PlatformDocumentId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}
