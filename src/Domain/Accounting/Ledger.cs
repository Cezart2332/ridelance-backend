using Domain.PfaRegistrations;
using SharedKernel;

namespace Domain.Accounting;

/// <summary>
/// O înregistrare contabilă internă (Accounting Ledger, B6) — <c>AccountingEntry</c> din spec-ul
/// fluxului contabil. Registrele RJIP, REF și Registru-inventar sunt proiecții din ea.
/// </summary>
/// <remarks>
/// <para>
/// O operațiune economică e o singură înregistrare, oricâte surse o conțin. Direcția vine din
/// semnul lui <see cref="Amount"/> (încasare &gt; 0, plată &lt; 0), iar canalul din
/// <see cref="PaymentMethod"/>.
/// </para>
/// <para>
/// Invarianții (<see cref="LedgerInvariants"/>) se verifică la fiecare salvare: partea personală și
/// suma deductibilă stau în limitele sumei, iar partea personală nu e niciodată deductibilă.
/// </para>
/// </remarks>
public sealed class LedgerEntry : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public DateOnly Date { get; set; }

    /// <summary>Documentul justificativ, cum apare în RJIP („Extras 10.10.2026”, „Raport Z nr. 125”).</summary>
    public string DocumentLabel { get; set; } = string.Empty;

    /// <summary>Fișierul justificativ, în tabelul de documente.</summary>
    public Guid? SourceDocumentId { get; set; }

    /// <summary>Documentul Uber/Bolt din care vine înregistrarea (legătura ledger ↔ declarații).</summary>
    public Guid? PlatformDocumentId { get; set; }

    /// <summary>Tranzacția bancară din Open Banking, pentru importurile din bancă.</summary>
    public Guid? BankTransactionId { get; set; }

    public LedgerSource Source { get; set; }

    /// <summary>Cheia de idempotență a importului, unică împreună cu <see cref="Source"/>.</summary>
    public string? ExternalId { get; set; }
    public string? Counterparty { get; set; }
    public string Description { get; set; } = string.Empty;
    public LedgerTransactionType TransactionType { get; set; }
    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>Pozitivă la încasări, negativă la plăți.</summary>
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "RON";

    /// <summary>Codul din <see cref="ExpenseCategoryRule"/>, doar la cheltuieli.</summary>
    public string? Category { get; set; }
    public bool VehicleRelated { get; set; }
    public DeductibilityType? DeductibilityType { get; set; }
    public decimal? DeductiblePercent { get; set; }
    public decimal? DeductibleAmount { get; set; }

    /// <summary>Setarea PFA (<c>vehicle_deductibility</c>) folosită la calcul, dacă e cazul.</summary>
    public Guid? DeductibilitySettingId { get; set; }
    public Guid? DeductibilityRuleId { get; set; }

    /// <summary>Data de la care e valabilă regula sau setarea aplicată.</summary>
    public DateOnly? DeductibilityValidFrom { get; set; }

    public LedgerEntryStatus Status { get; set; } = LedgerEntryStatus.AutoImported;

    /// <summary>Luna contabilă, <c>yyyy-MM</c>.</summary>
    public string AccountingPeriod { get; set; } = string.Empty;

    /// <summary>Import căzut într-o perioadă închisă: nu modifică luna, intră la verificare (B6).</summary>
    public bool ClosedPeriodFlag { get; set; }

    /// <summary>Cât de sigur e legată de documente și bani. Starea de lucru rămâne în <see cref="Status"/>.</summary>
    public ReconciliationStatus ReconciliationStatus { get; set; } = ReconciliationStatus.Matched;

    /// <summary>
    /// Leagă venitul brut și comisionul de același payout (R21). Doar înregistrările din același grup
    /// pot împărți o tranzacție bancară, iar suma lor e suma tranzacției.
    /// </summary>
    public Guid? SettlementGroupId { get; set; }

    /// <summary>Factura primită prin e-Factura pe care o plătește înregistrarea (R04, R04b).</summary>
    public Guid? EFacturaMessageId { get; set; }

    /// <summary>Data documentului justificativ, dacă diferă de data operațiunii (factura plătită mai târziu).</summary>
    public DateOnly? DocumentDate { get; set; }

    /// <summary>
    /// Partea personală a sumei, în valoare absolută (bonul cu 200 carburant + 50 personal, R30).
    /// Nu e niciodată deductibilă.
    /// </summary>
    public decimal PersonalAmount { get; set; }

    /// <summary>Partea din activitate, în valoare absolută: suma minus partea personală.</summary>
    public decimal BusinessAmount => Math.Abs(Amount) - PersonalAmount;

    /// <summary>Partea nedeductibilă a unei cheltuieli cu deductibilitate stabilită, inclusiv partea personală.</summary>
    public decimal? NonDeductibleAmount => DeductibleAmount is { } deductible ? Math.Abs(Amount) - deductible : null;

    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public PfaRegistration PfaRegistration { get; set; } = null!;
}

/// <summary>Un document de cheltuială încărcat, cu extracția și potrivirea propusă (B6).</summary>
public sealed class ExpenseDocument : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public Guid DocumentId { get; set; }
    public string? Merchant { get; set; }
    public string? MerchantCui { get; set; }
    public DateOnly? Date { get; set; }
    public decimal? Total { get; set; }

    /// <summary>JSON: produsele citite.</summary>
    public string ItemsJson { get; set; } = "[]";

    /// <summary>Tranzacția confirmată de utilizator ca potrivire.</summary>
    public Guid? LedgerEntryId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Un raport Z al casei de marcat, legat de încasarea din ledger (B6).</summary>
public sealed class ZReport : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public DateOnly Date { get; set; }
    public string ZNumber { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? LedgerEntryId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Un activ din Registrul-inventar (B7). Prefixat ca restul entităților PFA (<c>PfaVehicle</c>).</summary>
public sealed class PfaAsset : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateOnly AcquisitionDate { get; set; }
    public decimal AcquisitionValue { get; set; }
    public Guid? DocumentId { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.InUse;
    public DateOnly? DisposedDate { get; set; }
}
