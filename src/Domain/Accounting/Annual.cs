using System.Text.Json.Serialization;
using SharedKernel;

namespace Domain.Accounting;

/// <summary>
/// Contractul de închiriere de la o persoană fizică (spec declarații F40–F43): ramura D205 există doar
/// pentru PFA-urile care au unul. CNP-ul proprietarului e criptat la repaus.
/// </summary>
public sealed class RentalContract : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public string OwnerName { get; set; } = string.Empty;

    /// <summary>CNP-ul proprietarului, criptat (<c>ISecretProtector</c>).</summary>
    public string OwnerCnpEncrypted { get; set; } = string.Empty;
    public string ContractNumber { get; set; } = string.Empty;
    public DateOnly ContractDate { get; set; }
    public decimal GrossRent { get; set; }

    /// <summary><c>MONTHLY</c>, <c>QUARTERLY</c>, <c>YEARLY</c>.</summary>
    public string PaymentFrequency { get; set; } = "MONTHLY";

    /// <summary>Regula de reținere (<see cref="TaxRule"/> de tip <c>RentWithholding</c>, niciodată una de nerezident: F43).</summary>
    public Guid WithholdingRuleId { get; set; }

    /// <summary>Contractul încărcat (fișierul din dosarul PFA).</summary>
    public Guid? ContractDocumentId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>O plată de chirie: determină luna rândului D100 (F41) și intră în D205 anual (F42).</summary>
public sealed class RentPayment : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid RentalContractId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public decimal GrossAmount { get; set; }
    public Guid? LedgerEntryId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Răspunsurile anuale pentru D212 (spec declarații F53–F54): alte venituri sau contribuții în afara
/// RIDElance, completarea formularului suplimentar și netul din precompletarea ANAF.
/// </summary>
public sealed class AnnualTaxAnswers : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public int TaxYear { get; set; }

    /// <summary><c>null</c> = fără răspuns: D212 e blocată.</summary>
    public bool? HasExternalIncome { get; set; }

    /// <summary>Formularul suplimentar (la „Da”) e completat și verificat de contabil.</summary>
    public bool SupplementCompleted { get; set; }

    /// <summary>Venitul net din precompletarea ANAF, când e disponibil: control, nu înlocuiește calculul.</summary>
    public decimal? AnafPrefilledNetIncome { get; set; }
    public DateTime? AnsweredAtUtc { get; set; }
    public Guid? AnsweredByUserId { get; set; }
}

/// <summary>Starea cererii C801 (NUI / număr de ordine AMEF) a casei de marcat (spec declarații F60–F61).</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<C801Status>))]
public enum C801Status
{
    NotStarted = 0,

    /// <summary>Furnizorul casei de marcat a depus C801; RIDElance păstrează doar documentul și NUI-ul.</summary>
    FiledByProvider = 1,
    Filed = 2,
    NuiReceived = 3,
}
