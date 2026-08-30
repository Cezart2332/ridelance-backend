using Domain.Companies;

namespace Application.Companies;

/// <summary>Profilul firmei, așa cum îl vede proprietarul în dashboard.</summary>
// `LogoUrl` e o cale relativă servită de API (`/uploads/companies/...`), nu un URI absolut:
// `Uri` ar fi obligat fiecare consumator să reconstruiască originea.
#pragma warning disable CA1054
public sealed record CompanyProfileDto(
    Guid Id,
    string OwnerType,
    string LegalName,
    string? Cui,
    string? RegCom,
    string? LegalRepresentative,
    string? RegisteredOffice,
    /// <summary>Contul în care se încasează chiriile. Intră în contracte și facturi.</summary>
    string? Iban,
    string? Phone,
    string? Email,
    string? Website,
    string? PublicDescription,
    /// <summary>Fraza scurtă de sub denumire, în antetul mini-site-ului.</summary>
    string? Tagline,
    string? LogoUrl,
    /// <summary>Fotografia de fundal a mini-site-ului. Cale relativă, ca logo-ul.</summary>
    string? CoverImageUrl,
    /// <summary>Culorile mini-site-ului. Niciodată null — fără nimic salvat, sunt cele implicite.</summary>
    CompanyPageTheme PageTheme,
    /// <summary>Conținutul secțiunilor proprii ale mini-site-ului.</summary>
    CompanyPageContent PageContent,
    /// <summary>Documentul cu specimenul de semnătură, dacă a fost salvat unul.</summary>
    Guid? SignatureDocumentId,
    string Slug,
    bool IsVerified,
    VisibilityDto Visibility);
#pragma warning restore CA1054

/// <summary>Ce anume din datele de contact e public pe mini-site și pe anunțuri.</summary>
public sealed record VisibilityDto(bool Phone, bool Email, bool WhatsApp, bool Location);

public static class CompanyProfileMapper
{
    public static CompanyProfileDto ToDto(CompanyProfile profile) => new(
        profile.Id,
        profile.OwnerType.ToString(),
        profile.LegalName,
        profile.Cui,
        profile.RegCom,
        profile.LegalRepresentative,
        profile.RegisteredOffice,
        profile.Iban,
        profile.Phone,
        profile.Email,
        profile.Website,
        profile.PublicDescription,
        profile.Tagline,
        profile.LogoUrl,
        profile.CoverImageUrl,
        profile.PageTheme,
        profile.PageContent,
        profile.SignatureDocumentId,
        profile.Slug,
        profile.IsVerified,
        new VisibilityDto(profile.ShowPhone, profile.ShowEmail, profile.ShowWhatsApp, profile.ShowLocation));
}
