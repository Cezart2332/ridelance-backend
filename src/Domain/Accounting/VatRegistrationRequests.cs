using System.Text.Json.Serialization;
using Domain.PfaRegistrations;
using SharedKernel;

namespace Domain.Accounting;

/// <summary>Drumul unei cereri D700 pentru codul de TVA art. 317, de la generare la cod primit.</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<VatRegistrationStatus>))]
public enum VatRegistrationStatus
{
    /// <summary>Lipsesc date (de ex. CUI-ul, cât PFA-ul nu e încă înființat); nu există XML.</summary>
    WaitingForData = 0,

    /// <summary>XML-ul e generat, încă neverificat cu validatorul ANAF.</summary>
    Generated = 1,

    ValidationFailed = 2,

    /// <summary>Trecut de validatorul ANAF, cu PDF; așteaptă contabilul.</summary>
    ReadyForReview = 3,

    /// <summary>Contabilul l-a aprobat: se semnează și se depune.</summary>
    Approved = 4,

    /// <summary>Contabilul l-a respins, cu motiv; se regenerează după corectarea datelor.</summary>
    Rejected = 5,

    Submitted = 6,

    /// <summary>ANAF a atribuit codul; e scris în setările contabile și în profilul fiscal.</summary>
    Registered = 7,
}

/// <summary>
/// Cererea D700 („Declarație de mențiuni”, secțiunea B.VI, pct. 1.23.1 opțiunea 3) prin care un
/// PFA fără cod de TVA intracomunitar îl obține pentru serviciile primite de la Uber și Bolt.
/// Se generează automat când clientul răspunde „Nu” în onboarding; nu se completează de mână.
///
/// E separată de <see cref="Declaration"/>: acolo totul e lunar (o declarație de un tip pe lună,
/// generată din calculul lunii), iar D700 se depune o singură dată.
/// </summary>
public sealed class VatRegistrationRequest : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    public VatRegistrationStatus Status { get; set; }

    /// <summary>Luna cererii (<c>yyyy-MM</c>), atributele <c>an</c> și <c>luna</c> din XML.</summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>JSON: datele din XML în momentul generării (CUI, denumire, declarant).</summary>
    public string SnapshotJson { get; set; } = "{}";

    /// <summary>De ce nu s-a putut genera (<see cref="VatRegistrationStatus.WaitingForData"/>).</summary>
    public string? MissingData { get; set; }

    public Guid? XmlDocumentId { get; set; }
    public Guid? PdfDocumentId { get; set; }

    /// <summary>JSON: erorile și atenționările validatorului ANAF.</summary>
    public string? ValidationJson { get; set; }

    public string? RejectionReason { get; set; }

    /// <summary>Codul atribuit de ANAF (<c>RO…</c>), la <see cref="VatRegistrationStatus.Registered"/>.</summary>
    public string? VatCode { get; set; }
    public DateOnly? VatCodeValidFrom { get; set; }
    public Guid? CertificateDocumentId { get; set; }

    /// <summary>JSON: <c>[{ from, to, at, byUserId, note }]</c>.</summary>
    public string StatusHistoryJson { get; set; } = "[]";

    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public PfaRegistration PfaRegistration { get; set; } = null!;
}
