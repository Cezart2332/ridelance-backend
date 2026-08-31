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
    /// <summary>Locul de preluare, cu pin pe hartă. Separat de sediul social.</summary>
    PickupLocationDto Pickup,
    /// <summary>Documentul cu specimenul de semnătură, dacă a fost salvat unul.</summary>
    Guid? SignatureDocumentId,
    string Slug,
    bool IsVerified,
    VisibilityDto Visibility,
    /// <summary>Unde a ajuns pagina în verificare și ce secțiuni i-a oprit administrarea.</summary>
    CompanyPageModerationDto PageModeration);
#pragma warning restore CA1054

/// <summary>
/// Locul de unde se preiau mașinile.
/// </summary>
/// <remarks>
/// Coordonatele lipsesc când proprietarul n-a pus încă pinul. Adresa poate exista fără ele —
/// cineva scrie „București, Sector 3" fără să deschidă harta — iar secțiunea de pe mini-site se
/// descurcă și așa: arată textul, fără hartă.
/// </remarks>
public sealed record PickupLocationDto(
    string? Address,
    double? Latitude,
    double? Longitude,
    string? Note);

/// <summary>Ce anume din datele de contact e public pe mini-site și pe anunțuri.</summary>
public sealed record VisibilityDto(bool Phone, bool Email, bool WhatsApp, bool Location);

/// <summary>
/// Starea verificării mini-site-ului, așa cum o vede proprietarul.
/// </summary>
/// <remarks>
/// <paramref name="Note" /> e motivul scris de administrare. Ajunge la proprietar dinadins: un
/// refuz pe care nu-l poate remedia îl face să retrimită aceeași pagină.
///
/// <paramref name="PublishedAtUtc" /> e ce diferențiază „încă n-am publicat niciodată" de
/// „versiunea veche e live cât timp o verificăm pe cea nouă" — două stări care arată la fel în
/// statut, dar înseamnă lucruri opuse pentru vizitator.
/// </remarks>
public sealed record CompanyPageModerationDto(
    string Status,
    IReadOnlyList<string> BlockedSections,
    string? Note,
    DateTime? SubmittedAtUtc,
    DateTime? ReviewedAtUtc,
    DateTime? PublishedAtUtc);

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
        new PickupLocationDto(
            profile.PickupAddress,
            profile.PickupLatitude,
            profile.PickupLongitude,
            profile.PickupNote),
        profile.SignatureDocumentId,
        profile.Slug,
        profile.IsVerified,
        new VisibilityDto(profile.ShowPhone, profile.ShowEmail, profile.ShowWhatsApp, profile.ShowLocation),
        ToModerationDto(profile));

    public static CompanyPageModerationDto ToModerationDto(CompanyProfile profile) => new(
        profile.PageModeration.Status.ToString(),
        profile.PageModeration.BlockedSections,
        profile.PageModeration.Note,
        profile.PageModeration.SubmittedAtUtc,
        profile.PageModeration.ReviewedAtUtc,
        profile.PublishedPage.ApprovedAtUtc);
}
