using Domain.Documents;
using Domain.PfaRegistrations;
using SharedKernel;

namespace Domain.Accounting;

/// <summary>O declarație (D100, D301, D390) a unui PFA pentru o lună. Conținutul e în versiuni.</summary>
public sealed class Declaration : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    /// <summary>Luna fiscală, <c>yyyy-MM</c>.</summary>
    public string Period { get; set; } = string.Empty;
    public DeclarationType Type { get; set; }

    public PfaRegistration PfaRegistration { get; set; } = null!;
    public ICollection<DeclarationVersion> Versions { get; } = [];
}

/// <summary>
/// O versiune a declarației: inițială sau rectificativă. O versiune cu recipisă rămâne neschimbată;
/// corecția creează o versiune nouă (spec contabilitate §3.2, B5).
/// </summary>
public sealed class DeclarationVersion : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid DeclarationId { get; set; }
    public int VersionNo { get; set; }
    public DeclarationVersionKind Kind { get; set; }
    public DeclarationStatus Status { get; set; } = DeclarationStatus.Generated;

    /// <summary>Schema ANAF aleasă după perioada declarației, nu după data curentă.</summary>
    public Guid? SchemaId { get; set; }

    /// <summary>De plată. D390: mereu 0.</summary>
    public decimal Amount { get; set; }

    /// <summary>JSON: intrările și calculul din momentul generării. Regulile schimbate ulterior nu-l ating.</summary>
    public string SnapshotJson { get; set; } = "{}";

    public Guid? XmlDocumentId { get; set; }
    public Guid? PdfDocumentId { get; set; }

    /// <summary>JSON: rezultatul celor 3 niveluri de validare (brut și parsat).</summary>
    public string? ValidationResultJson { get; set; }

    public Guid? ReceiptDocumentId { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? RectificationReason { get; set; }

    /// <summary>JSON: <c>[{ from, to, at, byUserId, note }]</c>.</summary>
    public string StatusHistoryJson { get; set; } = "[]";

    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Declaration Declaration { get; set; } = null!;
    public AnafDeclarationSchema? Schema { get; set; }
    public Document? XmlDocument { get; set; }
    public Document? PdfDocument { get; set; }
    public Document? ReceiptDocument { get; set; }
    public ICollection<DeclarationLine> Lines { get; } = [];
}

/// <summary>
/// O linie de calcul: <c>bază × cotă = valoare</c>, cu documentul sursă. Relația document ↔
/// declarații se derivă de aici (B0). Datele furnizorului sunt copiate, ca linia să rămână
/// corectă și după ce registrul de furnizori se schimbă.
/// </summary>
public sealed class DeclarationLine : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid DeclarationVersionId { get; set; }
    public Guid SourceDocumentId { get; set; }
    public string RuleCode { get; set; } = string.Empty;

    public decimal Base { get; set; }
    public decimal? Rate { get; set; }
    public decimal Value { get; set; }
    public string Currency { get; set; } = "RON";
    public decimal? ExchangeRate { get; set; }
    public string Explanation { get; set; } = string.Empty;

    public string SupplierName { get; set; } = string.Empty;
    public string SupplierCountry { get; set; } = string.Empty;
    public string SupplierVatId { get; set; } = string.Empty;

    /// <summary>D390: tipul operațiunii (<c>S</c> pentru servicii).</summary>
    public string? OperationType { get; set; }
    public string? Treaty { get; set; }
    public DateOnly? ResidenceCertValidFrom { get; set; }
    public DateOnly? ResidenceCertValidTo { get; set; }

    public DeclarationVersion DeclarationVersion { get; set; } = null!;
    public PlatformDocument SourceDocument { get; set; } = null!;
}
