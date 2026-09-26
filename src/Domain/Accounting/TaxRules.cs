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
