using Domain.PfaRegistrations;
using SharedKernel;

namespace Domain.Accounting;

/// <summary>Cheile setărilor contabile versionate (spec contabilitate F5).</summary>
public static class PfaAccountingSettingKeys
{
    public const string Art317 = "art317";
    public const string Platforms = "platforms";
    public const string VehicleDeductibility = "vehicle_deductibility";
}

/// <summary>
/// O valoare a unei setări contabile, valabilă de la o dată. Append-only: o valoare nouă doar
/// închide intervalul celei vechi, nu o suprascrie (spec contabilitate B0, F5).
/// </summary>
public sealed class PfaAccountingSetting : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    /// <summary>Una din <see cref="PfaAccountingSettingKeys"/>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>JSON: <c>true</c>, <c>["BOLT","UBER"]</c>, <c>"50_PERCENT"</c>.</summary>
    public string ValueJson { get; set; } = "null";
    public DateOnly ValidFrom { get; set; }

    /// <summary>Observația / justificarea, obligatorie.</summary>
    public string Note { get; set; } = string.Empty;
    public Guid ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;

    public PfaRegistration PfaRegistration { get; set; } = null!;
}

/// <summary>Starea casei de marcat și a plăților în numerar ale unui PFA (§3.4, F7).</summary>
public sealed class CashRegisterState : Entity, IAccountingRecord
{
    public Guid PfaRegistrationId { get; set; }

    /// <summary>Răspunsul DA din onboarding (pasul 3).</summary>
    public bool CashRequested { get; set; }
    public DateTime? CashRequestedAnsweredAtUtc { get; set; }
    public bool CashEnabled { get; set; }
    public CashRegisterStatus Status { get; set; } = CashRegisterStatus.NotRequiredCurrentConfiguration;
    public DateOnly? ActivationDate { get; set; }
    public Guid? VerifiedByUserId { get; set; }

    /// <summary>Dovada de fiscalizare, obligatorie pentru <c>Active</c>.</summary>
    public Guid? EvidenceDocumentId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public PfaRegistration PfaRegistration { get; set; } = null!;
}

/// <summary>Colaborarea contabilă cu un PFA: de când și, după inactivare, până când (B8).</summary>
public sealed class PfaAccountingEngagement : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public EngagementStatus Status { get; set; } = EngagementStatus.Active;

    public PfaRegistration PfaRegistration { get; set; } = null!;
}

/// <summary>
/// Perioada contabilă lunară a unui PFA (§3.5). Numită așa, nu <c>AccountingPeriod</c>, ca să nu se
/// confunde cu <c>Application.Accounting.AccountingPeriod</c> (fereastra lunară de documente).
/// </summary>
public sealed class PfaAccountingPeriod : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    /// <summary><c>yyyy-MM</c></summary>
    public string Period { get; set; } = string.Empty;
    public AccountingPeriodStatus Status { get; set; } = AccountingPeriodStatus.Open;
    public Guid? ClosedByUserId { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    public PfaRegistration PfaRegistration { get; set; } = null!;
}

/// <summary>
/// Rezultatul pre-check-ului unui PFA pe o lună (B3): gata, de verificat sau cu documente lipsă,
/// cu motivele. Lipsa rândului înseamnă „neprocesat”.
/// </summary>
public sealed class PfaMonthCheck : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    /// <summary><c>yyyy-MM</c></summary>
    public string Period { get; set; } = string.Empty;
    public PfaMonthStatus Status { get; set; }

    /// <summary>JSON: motivele, în ordinea de afișare (primul apare sub numele PFA-ului).</summary>
    public string ReasonsJson { get; set; } = "[]";
    public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>O corecție într-o perioadă închisă: doar ADMIN / ACCOUNTANT, cu motiv și audit (B8).</summary>
public sealed class PeriodCorrection : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public string Period { get; set; } = string.Empty;
    public Guid? LedgerEntryId { get; set; }

    /// <summary>JSON: câmpurile schimbate.</summary>
    public string ChangeJson { get; set; } = "{}";
    public string Reason { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Jurnalul de audit al modulului: cine, când, valoarea veche, cea nouă și motivul (§0 pct. 7).
/// </summary>
public sealed class AuditLog : Entity, IAccountingRecord
{
    public Guid Id { get; set; }

    /// <summary>PFA-ul de care ține modificarea; <c>null</c> pentru regulile globale.</summary>
    public Guid? PfaRegistrationId { get; set; }
    public string Entity { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Reason { get; set; }

    /// <summary><c>null</c> pentru acțiunile sistemului (importuri, joburi).</summary>
    public Guid? UserId { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Un job de lungă durată (procesarea lunii, generare, validare, dosar de predare), cu progres.</summary>
public sealed class BackgroundJob : Entity
{
    public Guid Id { get; set; }
    public BackgroundJobType Type { get; set; }
    public BackgroundJobStatus Status { get; set; } = BackgroundJobStatus.Queued;

    /// <summary>JSON: parametrii (perioada, PFA-ul).</summary>
    public string ParametersJson { get; set; } = "{}";
    public int ProgressDone { get; set; }
    public int ProgressTotal { get; set; }

    /// <summary>JSON: <c>{ results: [...], errors: [...] }</c>.</summary>
    public string ResultJson { get; set; } = "{\"results\":[],\"errors\":[]}";

    /// <summary>Fișierul produs (arhiva dosarului de predare).</summary>
    public Guid? FileDocumentId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAtUtc { get; set; }
}
