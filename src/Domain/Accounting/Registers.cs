using System.Text.Json.Serialization;
using SharedKernel;

namespace Domain.Accounting;

// Registrele obligatorii PFA (spec registre §5–§7): activele cu amortizarea lor, inventarierea și
// anul contabil. Registrele înseși nu au tabele: RJIP, REF, Registrul-inventar și Fișa MF se
// generează din ledger și din entitățile de aici.

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<AssetKind>))]
public enum AssetKind
{
    FixedAsset = 0,
    InventoryObject = 1,
}

/// <summary>
/// Starea unei achiziții față de mijloacele fixe (spec registre §6). Tax Engine o propune
/// (<c>Pending</c>, deductibil 0); decizia Adminului e definitivă.
/// </summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<FixedAssetReview>))]
public enum FixedAssetReview
{
    None = 0,

    /// <summary>Posibil mijloc fix: valoarea peste prag, natura nu e de consum. Deductibil 0 până la decizie.</summary>
    Pending = 1,

    /// <summary>Admin: cheltuială curentă, deductibilă după categorie.</summary>
    Expense = 2,

    /// <summary>Admin: mijloc fix. Plata rămâne nedeductibilă; se deduce amortizarea.</summary>
    FixedAsset = 3,

    /// <summary>Admin: obiect de inventar. Deductibil după categorie, apare la inventar.</summary>
    InventoryObject = 4,
}

/// <summary>Sursa venitului în REF (OMFP 3254/2017, pe an și sursă).</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<IncomeSource>))]
public enum IncomeSource
{
    Ridesharing = 0,
}

/// <summary>Luna în care începe amortizarea (spec registre Q3).</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<DepreciationStart>))]
public enum DepreciationStart
{
    /// <summary>Luna următoare punerii în funcțiune (Codul fiscal, art. 28).</summary>
    NextMonth = 0,
    SameMonth = 1,
}

/// <summary>Pragul de mijloc fix și regula de start a amortizării, cu perioadă de valabilitate.</summary>
public sealed class FixedAssetRule : Entity, IAccountingRecord, IValidityPeriod
{
    public Guid Id { get; set; }

    /// <summary>Valoarea de la care o achiziție e propusă ca mijloc fix (lei, partea din activitate).</summary>
    public decimal Threshold { get; set; }
    public DepreciationStart DepreciationStart { get; set; }

    /// <summary>Categoriile de consum, care nu sunt niciodată propuse (<c>FUEL|CAR_SERVICE</c>).</summary>
    public string ExcludedCategories { get; set; } = string.Empty;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    public bool Excludes(string? category) =>
        category is not null &&
        ExcludedCategories.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(category, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Un activ al PFA-ului (spec registre §6): mijloc fix sau obiect de inventar. Se creează doar după
/// decizia Adminului; <c>Active</c> doar cu clasă, durată și punere în funcțiune (la mijloc fix).
/// </summary>
public sealed class PfaAsset : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    /// <summary><c>MF-0001</c> sau <c>OI-0001</c>, secvență per PFA și fel.</summary>
    public string InventoryNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AssetKind Kind { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.PendingClassification;

    /// <summary>Plata achiziției în ledger; lipsește la bunurile aduse ca aport.</summary>
    public Guid? AcquisitionEntryId { get; set; }

    /// <summary>Documentul de achiziție, cum apare pe fișă („Factura 123 / 10.02.2026”).</summary>
    public string DocumentRef { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
    public DateOnly EntryDate { get; set; }
    public DateOnly? InServiceDate { get; set; }
    public decimal EntryValue { get; set; }

    /// <summary>Codul din Catalogul privind clasificarea și duratele normale (HG 2139/2004).</summary>
    public string? DepreciationClassCode { get; set; }
    public int? NormalLifeMonths { get; set; }
    public string Method { get; set; } = "Linear";

    /// <summary>
    /// Autoturism (transport de persoane, cel mult 9 locuri). Durata normală e atunci între
    /// <see cref="PassengerCarMinLifeMonths"/> și <see cref="PassengerCarMaxLifeMonths"/> luni.
    /// </summary>
    public bool IsPassengerCar { get; set; }

    /// <summary>
    /// Cât din amortizarea unei luni se deduce cel mult (lei). Gol = toată.
    /// </summary>
    /// <remarks>
    /// Codul fiscal (art. 28 alin. 14) plafonează amortizarea autoturismelor la 1.500 lei pe lună,
    /// dar exceptează vehiculele folosite pentru transport de persoane cu plată. Dacă mașina unui
    /// șofer de ridesharing intră sub excepție o stabilește contabilul, per activ — de aceea e un
    /// câmp, nu o regulă.
    /// </remarks>
    public decimal? MonthlyDeductionCap { get; set; }
    public DateOnly? DisposalDate { get; set; }
    public string? DisposalReason { get; set; }

    /// <summary>Fișierul documentului de achiziție.</summary>
    public Guid? DocumentId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Codul autoturismelor în Catalogul duratelor normale de funcționare (HG 2139/2004).</summary>
    public const string PassengerCarClassCode = "2.3.2.1.1";

    /// <summary>Autoturismele se amortizează în 4–6 ani.</summary>
    public const int PassengerCarMinLifeMonths = 48;
    public const int PassengerCarMaxLifeMonths = 72;

    /// <summary>Un mijloc fix e complet cu clasă, durată și punere în funcțiune; un obiect de inventar, cu punerea în funcțiune.</summary>
    public bool IsComplete => InServiceDate is not null &&
        (Kind == AssetKind.InventoryObject || DepreciationClassCode is { Length: > 0 } && NormalLifeMonths is > 0);
}

/// <summary>
/// O lună din planul de amortizare liniară. Liniile lunilor închise sunt blocate; cele viitoare se
/// recalculează la o modificare a activului, deci nu sunt înregistrări contabile nedistructibile.
/// </summary>
public sealed class DepreciationLine : Entity
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Partea din <see cref="Amount"/> care se deduce: toată, sau cel mult plafonul activului.</summary>
    public decimal DeductibleAmount { get; set; }
    public decimal Accumulated { get; set; }
    public decimal Remaining { get; set; }
    public bool IsLocked { get; set; }

    /// <summary><c>yyyy-MM</c></summary>
    public string Period => $"{Year:0000}-{Month:00}";
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<InventoryReason>))]
public enum InventoryReason
{
    ActivityStart = 0,
    YearEnd = 1,
    Cessation = 2,
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<InventoryStatus>))]
public enum InventoryStatus
{
    Draft = 0,
    AwaitingPfaConfirmation = 1,
    AwaitingAdminReview = 2,
    Final = 3,
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<InventoryItemStatus>))]
public enum InventoryItemStatus
{
    Prefilled = 0,
    Confirmed = 1,
    Adjusted = 2,
    Removed = 3,
    AddedManually = 4,
}

/// <summary>Grupele Registrului-inventar, în ordinea din registru.</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<InventoryCategory>))]
public enum InventoryCategory
{
    FixedAssets = 0,
    InventoryObjects = 1,
    Stocks = 2,
    Receivables = 3,
    Bank = 4,
    Cash = 5,
    Debts = 6,
}

/// <summary>
/// O inventariere (spec registre §5): snapshot la o dată, precompletat din sistem și final doar după
/// confirmarea PFA-ului și revizuirea Adminului. Registrul-inventar e rezultatul ei.
/// </summary>
public sealed class InventoryCount : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public DateOnly Date { get; set; }
    public InventoryReason Reason { get; set; }
    public InventoryStatus Status { get; set; } = InventoryStatus.Draft;
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
    public Guid? FinalizedByUserId { get; set; }

    /// <summary>PDF-ul Registrului-inventar la finalizare (spec: <c>SnapshotPdfPath</c>).</summary>
    public Guid? SnapshotDocumentId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<InventoryItem> Items { get; set; } = [];
}

public sealed class InventoryItem : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid InventoryCountId { get; set; }
    public InventoryCategory Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal SystemValue { get; set; }
    public decimal? ConfirmedValue { get; set; }

    /// <summary>Confirmat − sistem; orice diferență cere notă.</summary>
    public decimal Difference => (ConfirmedValue ?? SystemValue) - SystemValue;

    /// <summary>De unde vine valoarea: <c>Asset</c>, <c>BankAccount</c>, <c>EFacturaMessage</c>, <c>TaxObligation</c>, <c>Ledger</c>.</summary>
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public InventoryItemStatus Status { get; set; } = InventoryItemStatus.Prefilled;

    /// <summary>Numerarul și banca se confirmă obligatoriu: sistemul nu vede ce e în casă.</summary>
    public bool RequiresConfirmation { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Anul contabil (spec registre §7): se închide după 12/12 luni închise și inventarul final. La
/// închidere se salvează REF-ul final și pachetul anual; redeschiderea e doar a Adminului.
/// </summary>
public sealed class AccountingYear : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public int Year { get; set; }
    public AccountingPeriodStatus Status { get; set; } = AccountingPeriodStatus.Open;
    public Guid? ClosedByUserId { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    /// <summary>REF-ul final, cu drill-down, la închidere.</summary>
    public string? RefJson { get; set; }

    /// <summary>Pachetul anual: ZIP cu RJIP, REF, Registru-inventar, fișele MF și lista activelor.</summary>
    public Guid? PackageDocumentId { get; set; }
}

/// <summary>
/// Explicația Adminului pentru o diferență care nu se poate anula (spec registre §7): Z vs cash
/// platformă, payout-uri nereconciliate. Controlul lunii trece cu explicația salvată.
/// </summary>
public sealed class ReconciliationExplanation : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    /// <summary><c>yyyy-MM</c></summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>Controlul explicat (<c>PlatformCashVsZ</c>, <c>UnreconciledPayouts</c>).</summary>
    public string Control { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
