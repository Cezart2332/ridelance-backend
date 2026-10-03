using SharedKernel;

namespace Domain.Accounting;

// Regulile fiscale configurabile (spec contabilitate §0 pct. 3, §4.5). Nimic din ce ține de cote,
// praguri sau entități nu e scris în cod: totul vine de aici, cu perioadă de valabilitate. O regulă
// nu se șterge; se închide setând `ValidTo`.

/// <summary>Perioada de valabilitate a unei reguli, capete incluse. <c>ValidTo = null</c>: valabilă în continuare.</summary>
public interface IValidityPeriod
{
    DateOnly ValidFrom { get; }
    DateOnly? ValidTo { get; }
}

/// <summary>Entitatea platformei care facturează comisionul, cu regimul ei D100.</summary>
public sealed class SupplierTaxProfile : Entity, IAccountingRecord, IValidityPeriod
{
    public Guid Id { get; set; }
    public string SupplierName { get; set; } = string.Empty;

    /// <summary>Cod ISO de țară (<c>EE</c>, <c>NL</c>).</summary>
    public string Country { get; set; } = string.Empty;
    public string VatId { get; set; } = string.Empty;

    /// <summary>De ex. <c>COMMISSION</c>.</summary>
    public string IncomeType { get; set; } = "COMMISSION";

    /// <summary>Convenția de evitare a dublei impuneri.</summary>
    public string? Treaty { get; set; }

    /// <summary>Cota D100, în procente (2 = 2%). <c>null</c> cât timp nu e stabilită.</summary>
    public decimal? D100Rate { get; set; }

    /// <summary><c>false</c> blochează D100: „Cota D100 pentru {furnizor} nu e confirmată” (B2).</summary>
    public bool D100RateConfirmed { get; set; }

    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    public DateOnly? ResidenceCertValidFrom { get; set; }
    public DateOnly? ResidenceCertValidTo { get; set; }

    /// <summary>Certificatul de rezidență fiscală, în tabelul de documente.</summary>
    public Guid? ResidenceCertDocumentId { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// Ștergere logică: furnizorul dispare din registru, din verificări și din calcul, dar rândul
    /// rămâne (înregistrările fiscale nu se șterg fizic). Doar pentru furnizori nefolosiți în declarații.
    /// </summary>
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }
}

/// <summary>Cota standard de TVA (D301), după data exigibilității.</summary>
public sealed class VatRate : Entity, IAccountingRecord, IValidityPeriod
{
    public Guid Id { get; set; }

    /// <summary>În procente (21 = 21%).</summary>
    public decimal Rate { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
}

/// <summary>O regulă de calcul D100.</summary>
public sealed class D100Rule : Entity, IAccountingRecord, IValidityPeriod
{
    public Guid Id { get; set; }
    public D100RuleCode Code { get; set; }
    public bool Enabled { get; set; }

    /// <summary>Marcată DE CONFIRMAT cu contabilul: afișată, dar nu se aplică.</summary>
    public bool PendingConfirmation { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>JSON cu parametrii regulii (baza, sursa cotei).</summary>
    public string ParametersJson { get; set; } = "{}";

    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
}

/// <summary>Cursul unei valute la o dată. Sursa și ziua cursului sunt DE CONFIRMAT (§6 pct. 4).</summary>
public sealed class ExchangeRate : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly Date { get; set; }

    /// <summary>Lei pentru o unitate de valută.</summary>
    public decimal Rate { get; set; }
    public string Source { get; set; } = string.Empty;
}

/// <summary>Versiunea schemei ANAF folosită pentru o declarație, după perioada declarației (B4).</summary>
public sealed class AnafDeclarationSchema : Entity, IAccountingRecord, IValidityPeriod
{
    public Guid Id { get; set; }
    public DeclarationType DeclarationType { get; set; }
    public string Version { get; set; } = string.Empty;

    /// <summary>Calea XSD-ului oficial din repo (<c>Anaf/Schemas/{TIP}/{versiune}/…</c>), nu un document de utilizator.</summary>
    public string? XsdPath { get; set; }
    public string? ValidatorVersion { get; set; }

    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
}

/// <summary>Clasificarea deterministă a cheltuielilor: categorie → regim de deductibilitate (B6).</summary>
public sealed class ExpenseCategoryRule : Entity, IAccountingRecord, IValidityPeriod
{
    public Guid Id { get; set; }

    /// <summary>Codul categoriei (<c>FUEL</c>, <c>CAR_SERVICE</c>).</summary>
    public string Category { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>Legată de autovehicul: procentul vine din setarea <c>vehicle_deductibility</c> a PFA-ului.</summary>
    public bool VehicleRelated { get; set; }
    public DeductibilityType DefaultDeductibility { get; set; }

    /// <summary>Expresie regulată pe contrapartidă (sau MCC).</summary>
    public string? CounterpartyPattern { get; set; }

    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
}

/// <summary>
/// Termenul minim de păstrare a documentelor (B8): 1 iulie al anului următor + N ani − 1 zi.
/// Valorile vin din documentul clientului și sunt DE CONFIRMAT cu contabilul (§6 pct. 12).
/// </summary>
public sealed class RetentionPolicy : Entity, IAccountingRecord, IValidityPeriod
{
    public Guid Id { get; set; }
    public int YearsAfter { get; set; }
    public int StartMonth { get; set; }
    public int StartDay { get; set; }

    /// <summary>Confirmată de contabil; până atunci valorile sunt afișate ca neconfirmate.</summary>
    public bool Confirmed { get; set; }

    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
}

/// <summary>Tipurile de <see cref="TaxRule"/> (spec declarații §4).</summary>
public static class TaxRuleTypes
{
    /// <summary>Codul de obligație bugetară al unei declarații (<c>634</c> pentru D100, comisioane nerezidenți).</summary>
    public const string ObligationCode = "ObligationCode";

    /// <summary>Codul bugetar din XML-ul declarației.</summary>
    public const string BudgetCode = "BudgetCode";

    /// <summary>Termenul de depunere: <c>MONTHLY:25</c> (ziua din luna următoare), <c>ANNUAL:05-25</c>, <c>ANNUAL:02-LAST</c>.</summary>
    public const string Deadline = "Deadline";

    /// <summary>Statul e membru UE la dată (serviciile de la furnizori de aici intră în D301 și D390).</summary>
    public const string EuMember = "EuMember";

    /// <summary>Cota de impozit pe veniturile nerezidenților: pe furnizorul juridic (tratat) sau de fallback.</summary>
    public const string NonResidentRate = "NonResidentRate";

    /// <summary>Regula cursului de schimb: <c>SameDayOrPrevious</c> sau <c>PreviousPublication</c>, sursa în <c>LegalBasis</c>.</summary>
    public const string ExchangeRate = "ExchangeRate";

    /// <summary>Rotunjirea totalului unei declarații: <c>WholeLei</c> sau <c>None</c>.</summary>
    public const string Rounding = "Rounding";

    /// <summary>Pragul de materialitate (lei) pentru payout-urile nereconciliate: sub el avertisment, peste <c>Stop</c>.</summary>
    public const string Materiality = "Materiality";

    /// <summary>Reținerea la sursă pe chiria plătită unei persoane fizice (ramura D205).</summary>
    public const string RentWithholding = "RentWithholding";

    /// <summary>Un cod de completare al unui formular (de ex. tipul de activitate din C801).</summary>
    public const string FormCode = "FormCode";
}

/// <summary>
/// O regulă fiscală versionată (spec declarații §4): cote, praguri, coduri de obligație, termene.
/// Se leagă de jurisdicție, entitatea juridică a furnizorului (cod fiscal, niciodată brandul), tipul
/// de venit și dată. O regulă nu se șterge; se închide prin <see cref="ValidTo"/>.
/// </summary>
public sealed class TaxRule : Entity, IAccountingRecord, IValidityPeriod
{
    public Guid Id { get; set; }
    public string RuleType { get; set; } = string.Empty;

    /// <summary><c>RO</c>, sau țara tratatului / a furnizorului.</summary>
    public string Jurisdiction { get; set; } = "RO";
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string LegalBasis { get; set; } = string.Empty;
    public decimal? Rate { get; set; }
    public decimal? Threshold { get; set; }
    public string? Formula { get; set; }
    public string? DeclarationCode { get; set; }
    public string? AnafFormVersion { get; set; }
    public string? ValidatorVersion { get; set; }

    /// <summary>Codul fiscal al furnizorului (<c>EE102090374</c>), pentru reguli pe entitate. Niciodată brandul.</summary>
    public string? SupplierEntityKey { get; set; }

    /// <summary>Tipul venitului (<c>COMMISSION</c>, <c>RENT</c>).</summary>
    public string? IncomeType { get; set; }

    /// <summary>Confirmată juridic; o regulă neconfirmată duce la <c>NeedsLegalConfirmation</c>, nu se aplică singură.</summary>
    public bool Confirmed { get; set; }
}
