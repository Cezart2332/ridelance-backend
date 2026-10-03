using System.Text.Json.Serialization;
using SharedKernel;

namespace Domain.Accounting;

/// <summary>Starea unei decizii de impozit nerezident (spec declarații §4, F22–F23).</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<NonResidentDecisionStatus>))]
public enum NonResidentDecisionStatus
{
    /// <summary>Regula confirmată, aplicată automat (certificat valabil + regulă de tratat, sau fallback confirmat).</summary>
    Auto = 0,

    /// <summary>Lipsește certificatul la data plății sau regula e neconfirmată: D100 nu se depune până la confirmarea Adminului.</summary>
    NeedsLegalConfirmation = 1,

    /// <summary>Confirmată de Admin, cu motiv.</summary>
    Confirmed = 2,
}

/// <summary>
/// O plată către un nerezident (spec declarații §4): pentru Uber/Bolt, comisionul reținut la
/// decontarea payout-ului. <see cref="PaymentDate"/> determină luna D100 (F20), nu data facturii.
/// </summary>
public sealed class NonResidentPayment : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public Guid? BankTransactionId { get; set; }

    /// <summary>Înregistrarea comisionului din ledger (R21), dacă plata vine din decontare.</summary>
    public Guid? LedgerEntryId { get; set; }

    /// <summary>Factura de comision a furnizorului juridic; din ea vin denumirea, țara și codul fiscal.</summary>
    public Guid? CommissionInvoiceId { get; set; }
    public string SupplierLegalName { get; set; } = string.Empty;
    public string SupplierCountry { get; set; } = string.Empty;

    /// <summary>Codul fiscal al furnizorului (cheia regulii, niciodată brandul).</summary>
    public string SupplierTaxId { get; set; } = string.Empty;
    public DateOnly PaymentDate { get; set; }
    public decimal GrossIncomeRon { get; set; }
    public string IncomeType { get; set; } = "COMMISSION";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Decizia de impozit pentru o plată (spec declarații F21–F25): regula aplicată, cota, impozitul,
/// codul de obligație. O decizie confirmată nu se mai recalculează; una automată, da, cu regulile noi.
/// </summary>
public sealed class NonResidentTaxDecision : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }

    /// <summary>Furnizorul din registru al cărui certificat de rezidență era valabil la data plății.</summary>
    public Guid? ResidenceCertificateProfileId { get; set; }

    /// <summary>Regula de tratat pe furnizor (din registrul de furnizori), dacă s-a aplicat.</summary>
    public Guid? TreatyRuleId { get; set; }

    /// <summary>Regula aplicată: cea de tratat sau cea de fallback (<see cref="TaxRule"/>).</summary>
    public Guid AppliedRuleId { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxDue { get; set; }
    public string ObligationCode { get; set; } = string.Empty;
    public NonResidentDecisionStatus Status { get; set; }

    /// <summary>De ce cere confirmare sau ce regulă s-a aplicat, pentru Admin.</summary>
    public string Explanation { get; set; } = string.Empty;
    public Guid? ConfirmedByUserId { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public string? ConfirmationReason { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
