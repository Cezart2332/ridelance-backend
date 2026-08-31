using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars;
using Application.Cars.Queries.GetAllCars;
using Domain.Cars;
using Domain.Companies;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Companies.Queries.GetPublicCompany;

/// <summary>Mini-site-ul public al unei firme: <c>/f/{slug}</c> (spec §4.2).</summary>
public sealed record GetPublicCompanyQuery(string Slug) : IQuery<PublicCompanyDto>;

/// <remarks>
/// Datele de contact apar **doar** dacă proprietarul le-a marcat publice în Profil. Filtrarea se
/// face aici, nu în interfață: un câmp ascuns care ajunge totuși în răspunsul API e public, chiar
/// dacă nu se vede pe ecran.
///
/// Website-ul n-are comutator și nu are nevoie de unul: e adresa pe care firma o publică singură.
///
/// Textul, culorile, fotografia de cover și punctul de preluare vin din copia **aprobată**
/// (<see cref="Domain.Companies.CompanyPagePublication"/>), nu din ciorna de pe profil. Fără o
/// aprobare, pagina rămâne la ce nu se poate scrie liber: denumirea, mașinile deja verificate și
/// datele de contact marcate publice.
/// </remarks>
#pragma warning disable CA1054
public sealed record PublicCompanyDto(
    string LegalName,
    string Slug,
    string? LogoUrl,
    string? CoverImageUrl,
    string? Tagline,
    string? PublicDescription,
    bool IsVerified,
    string? Phone,
    string? Email,
    string? Website,
    bool WhatsAppEnabled,
    string? Location,
    CompanyPageTheme Theme,
    CompanyPageContent Content,
    PickupLocationDto Pickup,
    List<CarDto> Cars);
#pragma warning restore CA1054

internal sealed class GetPublicCompanyQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetPublicCompanyQuery, PublicCompanyDto>
{
    public async Task<Result<PublicCompanyDto>> Handle(
        GetPublicCompanyQuery query,
        CancellationToken cancellationToken)
    {
        CompanyProfile? profile = await context.CompanyProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == query.Slug, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<PublicCompanyDto>(
                Error.NotFound("Company.NotFound", "Pagina firmei nu a fost găsită."));
        }

        // Aceleași condiții ca lista publică de mașini: pagina firmei nu e o portiță prin care
        // apar anunțuri neaprobate sau dezactivate.
        List<Car> cars = await context.Cars
            .AsNoTracking()
            .Include(c => c.Images.OrderBy(i => i.DisplayOrder))
            .Include(c => c.Leads)
            .Where(c => c.PostedByUserId == profile.UserId)
            .Where(CarVisibility.IsPublic)
            .OrderBy(c => c.Status == CarStatus.Available ? 0 : 1)
            .ThenByDescending(c => c.RecommendationScore)
            .ThenByDescending(c => c.UpdatedAtUtc)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

        var owner = new CarOwnerDto(
            profile.UserId,
            profile.OwnerType.ToString(),
            profile.LegalName,
            profile.LogoUrl,
            profile.Slug,
            profile.IsVerified);

        var carDtos = cars
            .Select(c => CarDtoMapper.ToDto(c, postedByAdmin: false, viewsLast7Days: 0, owner: owner))
            .ToList();

        // Nicio versiune aprobată = pagina de bază. Nu 404: linkul „vezi flota" de pe fiecare anunț
        // duce aici, iar o firmă cu mașini aprobate care așteaptă verificarea paginii n-are de ce
        // să pară inexistentă.
        CompanyPagePublication published = profile.PublishedPage.ApprovedAtUtc.HasValue
            ? profile.PublishedPage
            : new CompanyPagePublication();

        var blocked = profile.PageModeration.BlockedSections.ToHashSet(StringComparer.Ordinal);

        return Result.Success(new PublicCompanyDto(
            profile.LegalName,
            profile.Slug,
            profile.LogoUrl,
            published.CoverImageUrl,
            published.Tagline,
            blocked.Contains(CompanyPageSections.About) ? null : published.PublicDescription,
            profile.IsVerified,
            profile.ShowPhone ? profile.Phone : null,
            profile.ShowEmail ? profile.Email : null,
            profile.Website,
            // WhatsApp are nevoie de numărul de telefon, deci butonul depinde de ambele setări.
            profile.ShowWhatsApp && profile.ShowPhone && !string.IsNullOrWhiteSpace(profile.Phone),
            profile.ShowLocation ? profile.RegisteredOffice : null,
            published.Theme,
            FilterBlocked(published.Content, blocked),
            // Fără comutator al proprietarului: locul de preluare se publică prin faptul că a fost
            // completat. Sediul social de mai sus rămâne cel supus lui `ShowLocation`.
            blocked.Contains(CompanyPageSections.Location)
                ? new PickupLocationDto(null, null, null, null)
                : new PickupLocationDto(
                    published.PickupAddress,
                    published.PickupLatitude,
                    published.PickupLongitude,
                    published.PickupNote),
            carDtos));
    }

    /// <summary>
    /// Scoate din răspuns secțiunile oprite din administrare.
    /// </summary>
    /// <remarks>
    /// Se golește conținutul, nu se adaugă un semnalizator de ascuns: secțiunile de pe mini-site
    /// apar deja doar dacă au ce arăta (<c>src/components/company/sections.ts</c>), deci o listă
    /// goală e exact felul în care pagina știe să nu deseneze nimic. Un câmp în plus ar fi
    /// însemnat două reguli de vizibilitate pentru aceeași secțiune.
    ///
    /// Filtrarea se face aici, nu în interfață: un text care pleacă în răspunsul API e public chiar
    /// dacă nimeni nu-l randează.
    /// </remarks>
    private static CompanyPageContent FilterBlocked(CompanyPageContent content, HashSet<string> blocked)
    {
        if (blocked.Count == 0)
        {
            return content;
        }

        bool schedule = blocked.Contains(CompanyPageSections.Schedule);

        return new CompanyPageContent
        {
            Highlights = blocked.Contains(CompanyPageSections.Highlights) ? [] : content.Highlights,
            Schedule = schedule ? [] : content.Schedule,
            CoverageAreas = schedule ? [] : content.CoverageAreas,
            CoverageNote = schedule ? null : content.CoverageNote,
            Faq = blocked.Contains(CompanyPageSections.Faq) ? [] : content.Faq,
        };
    }
}
