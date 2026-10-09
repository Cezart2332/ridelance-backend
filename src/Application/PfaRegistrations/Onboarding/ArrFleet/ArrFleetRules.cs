using System.Globalization;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.ArrFleet;

namespace Application.PfaRegistrations.Onboarding.ArrFleet;

/// <summary>
/// Regulile pasului „ARR &amp; Cont Flotă”, fără bază de date: ce acte se cer, ce lipsește la
/// trimitere, cât costă și ce document oficial cere fiecare status. Aceeași sursă pentru client,
/// admin și validarea de la trimitere.
/// </summary>
public static class ArrFleetRules
{
    public sealed record Requirement(string Label, DocumentCategory Category);

    public static readonly Requirement[] PersonalDocuments =
    [
        new("Aviz medical", DocumentCategory.AdeverintaMedicala),
        new("Aviz psihologic", DocumentCategory.AvizPsihologic),
        new("Cazier judiciar", DocumentCategory.CazierJudiciar),
    ];

    public static readonly Requirement PaymentProof = new("Dovada plății", DocumentCategory.DovadaPlataArr);

    public static readonly Requirement[] VehicleDocuments =
    [
        new("Talon cu ITP valabil", DocumentCategory.Talon),
        new("Asigurare RCA", DocumentCategory.RCA),
        new("Asigurare de călători și bagaje", DocumentCategory.AsigurareCalatori),
    ];

    /// <summary>CASCO e opțional: se poate încărca, dar nu ține trimiterea pe loc.</summary>
    public static readonly Requirement Casco = new("CASCO", DocumentCategory.Casco);

    /// <summary>Contractul cerut de modul de deținere. Null la proprietate.</summary>
    public static Requirement? OwnershipDocument(ArrFleetVehicleOwnership? ownership) => ownership switch
    {
        ArrFleetVehicleOwnership.Loan => new("Comodat autentificat la notariat", DocumentCategory.ContractComodat),
        ArrFleetVehicleOwnership.Rental => new("Contract de închiriere", DocumentCategory.ContractInchiriere),
        ArrFleetVehicleOwnership.Leasing => new("Contract de leasing", DocumentCategory.ContractLeasing),
        _ => null,
    };

    /// <summary>Contractele de vehicul — cele care se pot înlocui la schimbarea modului de deținere.</summary>
    public static readonly DocumentCategory[] OwnershipCategories =
    [
        DocumentCategory.ContractComodat,
        DocumentCategory.ContractInchiriere,
        DocumentCategory.ContractLeasing,
    ];

    /// <summary>Categoria în care intră fiecare document oficial încărcat de agent.</summary>
    public static DocumentCategory CategoryOf(ArrFleetOfficialDocument type) => type switch
    {
        ArrFleetOfficialDocument.TransportAuthorization => DocumentCategory.AutorizatieTransportAlternativ,
        ArrFleetOfficialDocument.CertifiedCopy => DocumentCategory.CopieConforma,
        ArrFleetOfficialDocument.UberBadge => DocumentCategory.EcusonUber,
        _ => DocumentCategory.EcusonBolt,
    };

    public static string LabelOf(ArrFleetOfficialDocument type) => type switch
    {
        ArrFleetOfficialDocument.TransportAuthorization => "Autorizație de transport",
        ArrFleetOfficialDocument.CertifiedCopy => "Copie conformă",
        ArrFleetOfficialDocument.UberBadge => "Ecuson Uber",
        _ => "Ecuson Bolt",
    };

    /// <summary>Valabilitatea implicită, când adminul nu scrie data expirării: 3 ani, respectiv 1 an.</summary>
    public static DateTime? DefaultExpiry(ArrFleetOfficialDocument type, DateTime issuedAtUtc) => type switch
    {
        ArrFleetOfficialDocument.TransportAuthorization => issuedAtUtc.AddYears(3),
        ArrFleetOfficialDocument.CertifiedCopy => issuedAtUtc.AddYears(1),
        _ => null,
    };

    public static string StatusLabel(ArrFleetStatus status) => status switch
    {
        ArrFleetStatus.Draft => "În completare",
        ArrFleetStatus.DocumentsSubmitted => "Documente primite",
        ArrFleetStatus.InReview => "În verificare",
        ArrFleetStatus.InProgress => "În lucru (ARR / conturi flotă)",
        ArrFleetStatus.AuthorizationIssued => "Autorizație obținută",
        ArrFleetStatus.CertifiedCopyIssued => "Copie conformă obținută",
        ArrFleetStatus.BadgesIssued => "Ecusoane gata",
        _ => "Finalizat",
    };

    public static IReadOnlyList<PfaPlatformProvider> SelectedProviders(ArrFleetPlatforms platforms)
    {
        var providers = new List<PfaPlatformProvider>();
        if (platforms.HasFlag(ArrFleetPlatforms.Uber))
        {
            providers.Add(PfaPlatformProvider.Uber);
        }

        if (platforms.HasFlag(ArrFleetPlatforms.Bolt))
        {
            providers.Add(PfaPlatformProvider.Bolt);
        }

        return providers;
    }

    public static ArrFleetPlatforms FlagOf(PfaPlatformProvider provider) =>
        provider == PfaPlatformProvider.Uber ? ArrFleetPlatforms.Uber : ArrFleetPlatforms.Bolt;

    /// <summary>Rândurile plății, în ordinea din explicație. Ecusonul apare doar pentru platformele alese.</summary>
    public static IReadOnlyList<PaymentLine> PaymentLines(ArrFleetPlatforms platforms)
    {
        var lines = new List<PaymentLine>
        {
            new("autorizația de transport (valabilă 3 ani)", ArrFleetPricing.TransportAuthorizationBani),
            new("copia conformă (valabilă 1 an)", ArrFleetPricing.CertifiedCopyBani),
        };

        if (platforms.HasFlag(ArrFleetPlatforms.Bolt))
        {
            lines.Add(new("ecusoane Bolt", ArrFleetPricing.BadgePerPlatformBani));
        }

        if (platforms.HasFlag(ArrFleetPlatforms.Uber))
        {
            lines.Add(new("ecusoane Uber", ArrFleetPricing.BadgePerPlatformBani));
        }

        return lines;
    }

    /// <summary>„Plata este o sumă întreagă formată din: 300 lei autorizația…, 8 lei ecusoane Uber.”</summary>
    public static string PaymentExplanation(ArrFleetPlatforms platforms) =>
        "Plata este o sumă întreagă formată din: " +
        string.Join(", ", PaymentLines(platforms).Select(l => $"{Lei(l.AmountBani)} lei {l.Label}")) + ".";

    public static string Lei(long bani) =>
        (bani / 100m).ToString(bani % 100 == 0 ? "#,0" : "#,0.00", CultureInfo.GetCultureInfo("ro-RO"));

    /// <summary>Un document care încă satisface o cerință: nerespins și neînlocuit.</summary>
    public static bool IsUsable(Document document) =>
        document.Status != DocumentStatus.Rejected && !document.IsSuperseded;

    public static Document? Latest(IEnumerable<Document> documents, DocumentCategory category) =>
        documents
            .Where(d => d.Category == category && IsUsable(d))
            .OrderByDescending(d => d.UploadedAtUtc)
            .FirstOrDefault();

    /// <summary>
    /// Dovada plății e pentru o sumă care între timp s-a schimbat (clientul a schimbat platformele
    /// după ce a încărcat-o).
    /// </summary>
    public static bool PaymentProofOutdated(ArrFleetApplication application, IEnumerable<Document> documents)
    {
        Document? proof = Latest(documents, PaymentProof.Category);
        return proof is not null
            && application.PaymentAmountChangedAtUtc is DateTime changedAt
            && proof.UploadedAtUtc < changedAt;
    }

    /// <summary>Ce mai lipsește până la trimitere, pe înțelesul clientului. Gol = se poate trimite.</summary>
    public static IReadOnlyList<string> Missing(
        ArrFleetApplication application,
        IReadOnlyList<PfaPlatformAccount> accounts,
        IReadOnlyList<Document> documents)
    {
        var missing = new List<string>();

        foreach (Requirement requirement in PersonalDocuments)
        {
            AddIfMissing(missing, documents, requirement);
        }

        if (application.Platforms == ArrFleetPlatforms.None)
        {
            missing.Add("platformele");
        }

        foreach (PfaPlatformProvider provider in SelectedProviders(application.Platforms))
        {
            PfaPlatformAccount? account = accounts.FirstOrDefault(a => a.Provider == provider && a.IsSelectedByUser);
            if (account?.DriverHasExistingAccount is null)
            {
                missing.Add($"contul de șofer {provider}");
            }
            else if (account.DriverHasExistingAccount == true
                && (!PlatformContactRules.IsValidEmail(account.DriverEmail) || string.IsNullOrWhiteSpace(account.DriverPhone)))
            {
                missing.Add($"emailul și telefonul contului {provider}");
            }
        }

        AddIfMissing(missing, documents, PaymentProof);
        if (PaymentProofOutdated(application, documents))
        {
            missing.Add("dovada plății pentru suma actuală");
        }

        if (application.VehicleOwnership is null)
        {
            missing.Add("modul de deținere a mașinii");
        }
        else if (OwnershipDocument(application.VehicleOwnership) is Requirement contract)
        {
            AddIfMissing(missing, documents, contract);
        }

        foreach (Requirement requirement in VehicleDocuments)
        {
            AddIfMissing(missing, documents, requirement);
        }

        return missing;
    }

    /// <summary>
    /// Documentul oficial fără de care adminul nu poate trece procedura în statusul cerut. Null =
    /// trecerea e permisă. Statusurile 4–6 (și finalizarea) cer actele de până la ele.
    /// </summary>
    public static string? MissingOfficialDocument(
        ArrFleetApplication application,
        ArrFleetStatus target,
        IReadOnlyList<Document> documents)
    {
        var required = new List<ArrFleetOfficialDocument>();

        if (target >= ArrFleetStatus.AuthorizationIssued)
        {
            required.Add(ArrFleetOfficialDocument.TransportAuthorization);
        }

        if (target >= ArrFleetStatus.CertifiedCopyIssued)
        {
            required.Add(ArrFleetOfficialDocument.CertifiedCopy);
        }

        if (target >= ArrFleetStatus.BadgesIssued)
        {
            if (application.Has(ArrFleetPlatforms.Uber))
            {
                required.Add(ArrFleetOfficialDocument.UberBadge);
            }

            if (application.Has(ArrFleetPlatforms.Bolt))
            {
                required.Add(ArrFleetOfficialDocument.BoltBadge);
            }
        }

        ArrFleetOfficialDocument? missing = required
            .Cast<ArrFleetOfficialDocument?>()
            .FirstOrDefault(type => Latest(documents, CategoryOf(type!.Value)) is null);

        return missing is null ? null : LabelOf(missing.Value);
    }

    private static void AddIfMissing(List<string> missing, IReadOnlyList<Document> documents, Requirement requirement)
    {
        if (Latest(documents, requirement.Category) is null)
        {
            missing.Add(requirement.Label);
        }
    }
}

/// <summary>Un rând din plată: ce acoperă și cât.</summary>
public sealed record PaymentLine(string Label, long AmountBani);
