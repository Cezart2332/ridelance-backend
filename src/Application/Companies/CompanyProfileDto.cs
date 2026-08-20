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
    string? Phone,
    string? Email,
    string? Website,
    string? PublicDescription,
    string? LogoUrl,
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
        profile.Phone,
        profile.Email,
        profile.Website,
        profile.PublicDescription,
        profile.LogoUrl,
        profile.Slug,
        profile.IsVerified,
        new VisibilityDto(profile.ShowPhone, profile.ShowEmail, profile.ShowWhatsApp, profile.ShowLocation));
}
