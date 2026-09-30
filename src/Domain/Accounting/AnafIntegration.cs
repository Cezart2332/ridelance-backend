using System.Text.Json.Serialization;
using Domain.PfaRegistrations;
using SharedKernel;

namespace Domain.Accounting;

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<AnafConnectionStatus>))]
public enum AnafConnectionStatus
{
    Active = 0,

    /// <summary>Refresh-ul a eșuat sau tokenul de refresh a expirat: adminul se reconectează.</summary>
    Expired = 1,

    Disconnected = 2,
}

/// <summary>
/// Conexiunea OAuth cu ANAF a împuternicitului (adminul), făcută o dată cu certificatul lui
/// calificat. Tokenurile sunt emise pentru certificat, nu pentru un CUI: aceeași conexiune dă
/// acces la e-Factura tuturor PFA-urilor pentru care titularul certificatului e împuternicit în SPV.
/// Tokenurile se păstrează criptate.
/// </summary>
public sealed class AnafConnection : Entity, IAccountingRecord
{
    public Guid Id { get; set; }

    /// <summary>Adminul care s-a conectat cu certificatul lui.</summary>
    public Guid UserId { get; set; }

    public AnafConnectionStatus Status { get; set; }
    public string AccessTokenProtected { get; set; } = string.Empty;
    public string RefreshTokenProtected { get; set; } = string.Empty;
    public DateTime AccessExpiresAtUtc { get; set; }
    public DateTime RefreshExpiresAtUtc { get; set; }

    public DateTime ConnectedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RefreshedAtUtc { get; set; }
    public DateTime? DisconnectedAtUtc { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// O autorizare pornită din aplicație: leagă răspunsul ANAF (callback-ul) de adminul care a
/// cerut-o și de pagina la care se întoarce.
/// </summary>
public sealed class AnafAuthorizationRequest : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public string State { get; set; } = string.Empty;
    public Guid UserId { get; set; }

    /// <summary>Calea din aplicație (<c>/admin?tab=…</c>) la care se revine după autorizare.</summary>
    public string ReturnPath { get; set; } = "/admin";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<AnafPfaLinkStatus>))]
public enum AnafPfaLinkStatus
{
    Active = 0,

    /// <summary>Certificatul împuternicitului nu are drept în SPV pe CUI-ul PFA-ului.</summary>
    NoAccess = 1,

    Disabled = 2,
}

/// <summary>Sincronizarea e-Factura pentru un PFA, pornită de admin din fișa clientului.</summary>
public sealed class AnafPfaLink : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public AnafPfaLinkStatus Status { get; set; }
    public Guid EnabledByUserId { get; set; }
    public DateTime EnabledAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncAtUtc { get; set; }
    public string? LastError { get; set; }

    public PfaRegistration PfaRegistration { get; set; } = null!;
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<EFacturaMessageKind>))]
public enum EFacturaMessageKind
{
    /// <summary>„FACTURA PRIMITA”: factură de la un furnizor.</summary>
    Received = 0,

    /// <summary>„FACTURA TRIMISA”: factură emisă de PFA (de ex. prin Oblio).</summary>
    Sent = 1,

    /// <summary>„ERORI FACTURA”: o factură trimisă, respinsă de ANAF.</summary>
    Error = 2,

    /// <summary>„MESAJ CUMPARATOR PRIMIT / TRANSMIS”.</summary>
    BuyerMessage = 3,

    Other = 4,
}

/// <summary>Un mesaj din e-Factura al unui PFA, cu arhiva ANAF și datele facturii citite din XML.</summary>
public sealed class EFacturaMessage : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    /// <summary>Id-ul mesajului la ANAF (<c>id</c> din listă; parametrul <c>descarcare?id=</c>).</summary>
    public string AnafMessageId { get; set; } = string.Empty;

    public EFacturaMessageKind Kind { get; set; }

    /// <summary>Tipul exact, ca la ANAF (<c>FACTURA PRIMITA</c>).</summary>
    public string AnafType { get; set; } = string.Empty;
    public DateTime AnafCreatedAtUtc { get; set; }
    public string? UploadId { get; set; }
    public string? Details { get; set; }

    /// <summary>Arhiva ZIP de la ANAF (factura XML și semnătura MF).</summary>
    public Guid? ZipDocumentId { get; set; }
    public Guid? PdfDocumentId { get; set; }
    public string? DownloadError { get; set; }
    public DateTime? DownloadedAtUtc { get; set; }

    /// <summary>Din XML-ul facturii (UBL / CIUS-RO); goale pentru erori și mesaje.</summary>
    public string? InvoiceNumber { get; set; }
    public DateOnly? IssueDate { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierCif { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerCif { get; set; }
    public string? Currency { get; set; }
    public decimal? TotalAmount { get; set; }
    public decimal? VatAmount { get; set; }

    /// <summary><c>true</c> pentru o notă de credit (<c>CreditNote</c>): PDF-ul se cere cu standardul FCN.</summary>
    public bool IsCreditNote { get; set; }

    /// <summary>
    /// Cât s-a plătit din factura primită, din plățile bancare legate de ea (spec flux contabil R03–R04b).
    /// Factura nu apare în RJIP; apar doar plățile ei.
    /// </summary>
    public decimal PaidAmount { get; set; }

    public InvoicePaymentStatus PaymentStatus { get; set; } = InvoicePaymentStatus.Unpaid;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
