using System.Text.Json.Serialization;
using SharedKernel;

namespace Domain.Accounting;

/// <summary>
/// Ce este o tranzacție bancară pentru contabilitate, ales sau confirmat de Admin (spec flux contabil
/// §6): determină tipul înregistrării, explicația din RJIP și efectul în REF.
/// </summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<BankClassification>))]
public enum BankClassification
{
    /// <summary>Titular → PFA: aport, nu venit (R41).</summary>
    OwnerContribution = 0,

    /// <summary>PFA → titular: utilizarea venitului, nu cheltuială (R40).</summary>
    OwnerWithdrawal = 1,

    /// <summary>Între conturile PFA-ului (R43).</summary>
    InternalTransfer = 2,

    /// <summary>Impozite și contribuții plătite la ANAF / Trezorerie (R42).</summary>
    TaxPayment = 3,

    /// <summary>Comisionul de administrare a contului bancar; extrasul e documentul.</summary>
    BankFee = 4,

    /// <summary>Venit impozabil din activitate (intră în REF).</summary>
    ActivityIncome = 5,

    /// <summary>Încasare fără efect fiscal: rambursare, sumă personală.</summary>
    NonTaxable = 6,

    /// <summary>Cheltuială din activitate: cere documentul justificativ.</summary>
    Expense = 7,
}

/// <summary>
/// Regula învățată pe contrapartidă („Aplică la toate similare”): tranzacțiile bancare cu același
/// nume normalizat (sau IBAN) și același sens primesc aceeași clasificare, fără să mai ajungă excepții.
/// </summary>
public sealed class CounterpartyClassificationRule : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    /// <summary><c>true</c> = încasări, <c>false</c> = plăți.</summary>
    public bool Incoming { get; set; }

    /// <summary>Numele contrapartidei (sau detaliile plății), normalizat, fără cifre și forme juridice.</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>IBAN-ul contrapartidei, normalizat, când banca îl dă.</summary>
    public string? Iban { get; set; }
    public BankClassification Classification { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
