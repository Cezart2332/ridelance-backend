using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Application.Abstractions.Data;
using Domain.Documents;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;

namespace Application.PfaRegistrations.Onboarding.ArrFleet;

/// <summary>Agenția ARR la care plătește clientul, sau de ce n-am putut-o stabili.</summary>
public sealed record ArrAgencyResolution(ArrAccount? Account, string? Error);

/// <summary>
/// Agenția teritorială ARR a clientului: cea din județul sediului social al PFA-ului. Plățile merg
/// direct în contul ei de trezorerie, deci un cont ghicit ar trimite banii în alt județ — fără
/// județ nu propunem niciun cont, ci spunem ce lipsește.
/// </summary>
internal sealed class ArrAgencyResolver(IApplicationDbContext context)
{
    public const string CountyMissing =
        "Nu știm județul sediului social al PFA-ului, deci nici agenția ARR la care plătești. Scrie-ne la suport și îl completăm.";

    public static string CountyUnknown(string county) =>
        $"Nu găsim agenția ARR pentru județul „{county}”. Scrie-ne la suport și îl corectăm.";

    public async Task<ArrAgencyResolution> ResolveAsync(PfaRegistration registration, CancellationToken cancellationToken)
    {
        string? county = await SeatCountyAsync(registration, cancellationToken);
        if (county is null)
        {
            return new ArrAgencyResolution(null, CountyMissing);
        }

        List<ArrAccount> accounts = await context.ArrAccounts
            .AsNoTracking()
            .Where(a => a.IsActive)
            .ToListAsync(cancellationToken);

        ArrAccount? account = ArrCountyMatcher.Match(county, accounts);
        return account is null
            ? new ArrAgencyResolution(null, CountyUnknown(county))
            : new ArrAgencyResolution(account, null);
    }

    /// <summary>
    /// Județul sediului social, în ordinea în care sursele sunt cele mai sigure: adresa sediului
    /// din dosarul de înființare (ramura „Nu am PFA”), județul citit de pe certificatul de
    /// înregistrare, apoi adresa sediului profesional de pe certificate. Nu domiciliul din buletin:
    /// agenția e a sediului, nu a omului.
    /// </summary>
    private async Task<string?> SeatCountyAsync(PfaRegistration registration, CancellationToken cancellationToken)
    {
        string? formationCounty = await context.CompanyFormationRequests
            .AsNoTracking()
            .Where(r => r.PfaRegistrationId == registration.Id)
            .Select(r => r.OfficeAddress.Judet)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(formationCounty))
        {
            return formationCounty.Trim();
        }

        List<Guid> certificates = await context.Documents
            .AsNoTracking()
            .Where(d => d.UserId == registration.UserId
                && (d.Category == DocumentCategory.CertificatInregistrare || d.Category == DocumentCategory.CertificatConstatator)
                && d.Status != DocumentStatus.Rejected)
            .OrderByDescending(d => d.UploadedAtUtc)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        List<ExtractedField> fields = await context.ExtractedFields
            .AsNoTracking()
            .Where(f => certificates.Contains(f.DocumentId) && (f.FieldKey == "judet" || f.FieldKey == "professional_office"))
            .ToListAsync(cancellationToken);

        string? Value(Guid documentId, string key)
        {
            ExtractedField? field = fields.Find(f => f.DocumentId == documentId && f.FieldKey == key);
            string? value = field?.ConfirmedValue ?? field?.AiNormalizedValue;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        foreach (Guid documentId in certificates)
        {
            if (Value(documentId, "judet") is string county)
            {
                return county;
            }
        }

        IEnumerable<string?> offices = certificates
            .Select(id => Value(id, "professional_office"))
            .Append(registration.ProfessionalOffice);

        foreach (string? office in offices)
        {
            if (ArrCountyMatcher.CountyInAddress(office) is string county)
            {
                return county;
            }
        }

        return string.IsNullOrWhiteSpace(registration.County) ? null : registration.County.Trim();
    }
}

/// <summary>Potrivirea unui județ scris oricum („CLUJ”, „jud. Cluj”, „Municipiul București”) cu agenția lui.</summary>
public static partial class ArrCountyMatcher
{
    [GeneratedRegex(@"\bJUD(?:ETUL|\.)?\s+([A-Z][A-Z\- ]+?)(?:,|$|\s+(?:MUN|ORAS|COM|LOC|SAT|STR|SECTOR)\b)")]
    private static partial Regex CountyPrefix();

    public static ArrAccount? Match(string county, IReadOnlyList<ArrAccount> accounts)
    {
        string key = Normalize(county);
        if (key.Length == 0)
        {
            return null;
        }

        // „Municipiul București”, „București Sector 3”: o singură agenție pentru tot orașul.
        if (key.Contains("BUCURESTI", StringComparison.Ordinal))
        {
            return accounts.FirstOrDefault(a => a.CountyCode == "B");
        }

        return accounts.FirstOrDefault(a => Normalize(a.CountyName) == key)
            ?? accounts.FirstOrDefault(a => string.Equals(a.CountyCode, key, StringComparison.Ordinal));
    }

    /// <summary>Județul dintr-o adresă scrisă ca text („…, Jud. Cluj, Mun. Cluj-Napoca”). Null fără mențiune explicită.</summary>
    public static string? CountyInAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        string normalized = Fold(address);
        if (normalized.Contains("BUCURESTI", StringComparison.Ordinal))
        {
            return "București";
        }

        Match match = CountyPrefix().Match(normalized);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>Fără diacritice, cu majuscule, fără „jud.”/„județul”/„municipiul”.</summary>
    public static string Normalize(string value)
    {
        string upper = Fold(value);
        foreach (string prefix in new[] { "JUDETUL ", "JUD. ", "JUD ", "MUNICIPIUL ", "MUN. " })
        {
            if (upper.StartsWith(prefix, StringComparison.Ordinal))
            {
                upper = upper[prefix.Length..];
            }
        }

        return upper;
    }

    /// <summary>Fără diacritice, cu majuscule și spațiile strânse. Prefixele rămân: adresa le folosește.</summary>
    private static string Fold(string value)
    {
        string decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return string.Join(' ', builder.ToString().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
