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
/// Culorile și conținutul secțiunilor pleacă întotdeauna — sunt tot ce face pagina să arate a
/// site-ul firmei, nu al nostru.
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

        return Result.Success(new PublicCompanyDto(
            profile.LegalName,
            profile.Slug,
            profile.LogoUrl,
            profile.CoverImageUrl,
            profile.Tagline,
            profile.PublicDescription,
            profile.IsVerified,
            profile.ShowPhone ? profile.Phone : null,
            profile.ShowEmail ? profile.Email : null,
            profile.Website,
            // WhatsApp are nevoie de numărul de telefon, deci butonul depinde de ambele setări.
            profile.ShowWhatsApp && profile.ShowPhone && !string.IsNullOrWhiteSpace(profile.Phone),
            profile.ShowLocation ? profile.RegisteredOffice : null,
            profile.PageTheme,
            profile.PageContent,
            carDtos));
    }
}
