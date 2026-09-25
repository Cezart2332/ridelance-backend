using System.Text.Json;
using Domain.PfaRegistrations.CompanyFormation;

namespace Application.Payments.ServiceOrders;

/// <summary>
/// Ce fel de dosar cere un serviciu individual.
///
/// Înființarea și Start Ride cer exact ce cere ramura „Nu am PFA" din onboarding: datele
/// personale, sediul social, acordurile și semnătura. Găzduirea sediului cere titularul și zona
/// aleasă din lista Consulto — ce se completează în onboarding când omul vrea sediu de la noi.
/// </summary>
public enum ServiceDossierKind
{
    Formation,
    RegisteredOfficeHosting,
}

public static class ServiceDossierKinds
{
    public const string InfiintarePfa = "infiintare_pfa";
    public const string SediuSocial = "sediu_social";
    public const string StartRide = "start_ride";

    public static ServiceDossierKind? For(string serviceKey) => serviceKey.ToUpperInvariant() switch
    {
        "INFIINTARE_PFA" or "START_RIDE" => ServiceDossierKind.Formation,
        "SEDIU_SOCIAL" => ServiceDossierKind.RegisteredOfficeHosting,
        _ => null,
    };

    /// <summary>Start Ride include înregistrarea în scopuri de TVA intracomunitar.</summary>
    public static bool IncludesVatIntracom(string serviceKey) =>
        string.Equals(serviceKey, StartRide, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Un acord dat în wizard, cu textul exact afișat atunci.</summary>
public sealed class ServiceOrderConsent
{
    public string StepKey { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string TextSnapshot { get; set; } = string.Empty;
    public string CheckboxLabelSnapshot { get; set; } = string.Empty;
    public DateTime AcceptedAtUtc { get; set; }
}

/// <summary>Un fișier al dosarului, salvat criptat, ca orice document.</summary>
public sealed class ServiceOrderFile
{
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string EncryptedFilePath { get; set; } = string.Empty;
    public string EncryptionIv { get; set; } = string.Empty;
    public long FileSize { get; set; }
}

/// <summary>Semnătura, cu probatoriul completat exclusiv de server.</summary>
public sealed class ServiceOrderSignature
{
    public ServiceOrderFile Image { get; set; } = new();
    public string? VectorData { get; set; }
    public int CanvasWidth { get; set; }
    public int CanvasHeight { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceType { get; set; }
    public string? Os { get; set; }
    public string? Browser { get; set; }
    public DateTime SignedAtUtc { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
}

/// <summary>
/// Dosarul unei comenzi de serviciu, păstrat ca JSON pe <see cref="Domain.Payments.ServiceOrder"/>.
/// Folosește aceleași tipuri ca dosarul din onboarding (<see cref="PersoanaFizica"/>,
/// <see cref="Adresa"/>), deci CNP-ul e criptat exact la fel.
/// </summary>
public sealed class ServiceOrderDossier
{
    private static readonly JsonSerializerOptions Json = new()
    {
        // `IsComplete`, `RequiresSerie` ș.a. sunt calculate: nu au ce căuta în JSON.
        IgnoreReadOnlyProperties = true,
    };

    public PersoanaFizica Solicitant { get; set; } = new();

    public RegisteredOfficeType? OfficeType { get; set; }
    public Guid? ConsultoOfficeId { get; set; }
    public bool? IsOwner { get; set; }
    public Adresa OfficeAddress { get; set; } = new();
    public bool AcknowledgedOwnershipDocs { get; set; }
    public bool AcknowledgedSubmitLater { get; set; }
    public bool? AcknowledgedOwnerConsent { get; set; }
    public List<PersoanaFizica> Owners { get; set; } = [];

    /// <summary>Doar la găzduirea sediului: PFA-ul care primește sediul, dacă există deja.</summary>
    public string? CompanyName { get; set; }
    public string? CompanyCui { get; set; }

    public bool IncludesVatIntracom { get; set; }

    public ServiceOrderFile? IdentityDocument { get; set; }
    public List<ServiceOrderConsent> Consents { get; set; } = [];
    public ServiceOrderSignature? Signature { get; set; }

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static ServiceOrderDossier? Parse(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<ServiceOrderDossier>(json, Json);

    /// <summary>
    /// Dosarul, ca dosar de înființare nesalvat. Regulile de completitudine, hash-ul semnăturii
    /// și fișele PDF sunt ale dosarului din onboarding; prin forma asta le folosim pe aceleași,
    /// fără o a doua copie care să diveargă.
    /// </summary>
    public CompanyFormationRequest ToFormationRequest(Guid id, ConsultoOffice? office)
    {
        var request = new CompanyFormationRequest
        {
            Id = id,
            Solicitant = Solicitant,
            OfficeType = OfficeType,
            ConsultoOfficeId = ConsultoOfficeId,
            ConsultoOffice = office,
            IsOwner = IsOwner,
            OfficeAddress = OfficeAddress,
            AcknowledgedOwnershipDocs = AcknowledgedOwnershipDocs,
            AcknowledgedSubmitLater = AcknowledgedSubmitLater,
            AcknowledgedOwnerConsent = AcknowledgedOwnerConsent,
            Owners = Owners
                .Select((persoana, position) => new CompanyFormationOwner
                {
                    Id = Guid.NewGuid(),
                    CompanyFormationRequestId = id,
                    Position = position,
                    Persoana = persoana,
                })
                .ToList(),
            Consents = Consents
                .Select(c => new CompanyFormationConsent
                {
                    Id = Guid.NewGuid(),
                    CompanyFormationRequestId = id,
                    StepKey = c.StepKey,
                    Version = c.Version,
                    TextSnapshot = c.TextSnapshot,
                    CheckboxLabelSnapshot = c.CheckboxLabelSnapshot,
                    AcceptedAtUtc = c.AcceptedAtUtc,
                })
                .ToList(),
        };

        if (Signature is not null)
        {
            request.Signature = new CompanyFormationSignature
            {
                Id = Guid.NewGuid(),
                CompanyFormationRequestId = id,
                VectorData = Signature.VectorData,
                CanvasWidth = Signature.CanvasWidth,
                CanvasHeight = Signature.CanvasHeight,
                IpAddress = Signature.IpAddress,
                UserAgent = Signature.UserAgent,
                DeviceType = Signature.DeviceType,
                Os = Signature.Os,
                Browser = Signature.Browser,
                SignedAtUtc = Signature.SignedAtUtc,
                PayloadHash = Signature.PayloadHash,
            };
        }

        return request;
    }
}
