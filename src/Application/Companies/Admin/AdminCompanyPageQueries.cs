using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars;
using Domain.Companies;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Companies.Admin;

/// <summary>
/// Coada de verificare a mini-site-urilor, pentru administrare.
/// </summary>
/// <remarks>
/// <paramref name="Status" /> filtrează după verdict (<c>Pending</c>, <c>Approved</c>,
/// <c>Rejected</c>, <c>Draft</c>); <c>null</c> le aduce pe toate. Ciornele goale nu se ascund
/// automat: o firmă care și-a golit pagina tot e o firmă despre care vrem să știm.
/// </remarks>
public sealed record GetAdminCompanyPagesQuery(string? Status, string? Search)
    : IQuery<IReadOnlyList<AdminCompanyPageListItem>>;

/// <summary>Un rând din listă: cât să încapă într-un card, fără conținutul paginii.</summary>
public sealed record AdminCompanyPageListItem(
    Guid ProfileId,
    Guid UserId,
    string LegalName,
    string Slug,
    string OwnerType,
    string? Cui,
    string OwnerEmail,
    string Status,
    IReadOnlyList<string> BlockedSections,
    DateTime? SubmittedAtUtc,
    DateTime? ReviewedAtUtc,
    DateTime? PublishedAtUtc,
    /// <summary>Câte mașini publice atârnă de pagina asta. Un refuz pe o flotă activă cântărește altfel.</summary>
    int PublicCarCount);

#pragma warning disable CA1054
/// <summary>Pagina unei firme, cu ciorna și versiunea publicată una lângă alta.</summary>
public sealed record GetAdminCompanyPageQuery(Guid ProfileId) : IQuery<AdminCompanyPageDetail>;

/// <remarks>
/// Ciorna și copia publicată se trimit amândouă. Cine verifică trebuie să vadă exact ce se schimbă
/// dacă apasă „Aprobă" — nu doar textul nou, ci și ce înlocuiește.
/// </remarks>
public sealed record AdminCompanyPageDetail(
    Guid ProfileId,
    Guid UserId,
    string LegalName,
    string Slug,
    string OwnerType,
    string? Cui,
    string OwnerEmail,
    string? Phone,
    string? Email,
    string? Website,
    string? LogoUrl,
    CompanyPageModerationDto Moderation,
    AdminCompanyPageVersion Draft,
    /// <summary><c>null</c> când pagina n-a fost publicată niciodată.</summary>
    AdminCompanyPageVersion? Published,
    int PublicCarCount);

/// <summary>O versiune a paginii — ciorna proprietarului sau copia aprobată.</summary>
public sealed record AdminCompanyPageVersion(
    string? Tagline,
    string? PublicDescription,
    string? CoverImageUrl,
    CompanyPageTheme Theme,
    CompanyPageContent Content,
    PickupLocationDto Pickup);
#pragma warning restore CA1054

internal sealed class GetAdminCompanyPagesQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetAdminCompanyPagesQuery, IReadOnlyList<AdminCompanyPageListItem>>
{
    public async Task<Result<IReadOnlyList<AdminCompanyPageListItem>>> Handle(
        GetAdminCompanyPagesQuery query,
        CancellationToken cancellationToken)
    {
        // Profilul și contul se citesc împreună; restul filtrării se face în memorie, pe starea din
        // jsonb. Sunt câteva sute de firme, nu milioane — o interogare care despachetează jsonb în
        // SQL ar fi fost mai greu de citit decât e de rapid.
        var rows = await context.CompanyProfiles
            .AsNoTracking()
            .Join(
                context.Users.AsNoTracking(),
                profile => profile.UserId,
                user => user.Id,
                (profile, user) => new { Profile = profile, User = user })
            .ToListAsync(cancellationToken);

        Dictionary<Guid, int> carCounts = await CountPublicCars(context, cancellationToken);

        IEnumerable<AdminCompanyPageListItem> items = rows
            .Select(row => new AdminCompanyPageListItem(
                row.Profile.Id,
                row.Profile.UserId,
                row.Profile.LegalName,
                row.Profile.Slug,
                row.Profile.OwnerType.ToString(),
                row.Profile.Cui,
                row.User.Email,
                row.Profile.PageModeration.Status.ToString(),
                row.Profile.PageModeration.BlockedSections,
                row.Profile.PageModeration.SubmittedAtUtc,
                row.Profile.PageModeration.ReviewedAtUtc,
                row.Profile.PublishedPage.ApprovedAtUtc,
                carCounts.GetValueOrDefault(row.Profile.UserId)));

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            items = items.Where(i => string.Equals(i.Status, query.Status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string needle = query.Search.Trim();
            items = items.Where(i =>
                i.LegalName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                i.Slug.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                i.OwnerEmail.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                (i.Cui ?? string.Empty).Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        // Cele care așteaptă un verdict, primele — restul e istorie. În fiecare grupă, cea mai
        // veche cerere sus: coada se golește de la capătul care așteaptă de cel mai mult timp.
        List<AdminCompanyPageListItem> ordered = [.. items
            .OrderBy(i => i.Status == nameof(CompanyPageReviewStatus.Pending) ? 0 : 1)
            .ThenBy(i => i.SubmittedAtUtc ?? DateTime.MaxValue)
            .ThenBy(i => i.LegalName, StringComparer.OrdinalIgnoreCase)];

        return Result.Success<IReadOnlyList<AdminCompanyPageListItem>>(ordered);
    }

    /// <summary>Câte anunțuri publice are fiecare proprietar, pe id de cont.</summary>
    internal static async Task<Dictionary<Guid, int>> CountPublicCars(
        IApplicationDbContext context,
        CancellationToken cancellationToken) =>
        await context.Cars
            .AsNoTracking()
            .Where(CarVisibility.IsPublic)
            // Anunțurile fără proprietar sunt cele puse de administrare. N-au profil de firmă, deci
            // n-au ce număra aici — și `PostedByUserId` fiind opțional, gruparea lor ar fi cerut o
            // cheie nulă în dicționar.
            .Where(c => c.PostedByUserId.HasValue)
            .GroupBy(c => c.PostedByUserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);
}

internal sealed class GetAdminCompanyPageQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetAdminCompanyPageQuery, AdminCompanyPageDetail>
{
    public async Task<Result<AdminCompanyPageDetail>> Handle(
        GetAdminCompanyPageQuery query,
        CancellationToken cancellationToken)
    {
        CompanyProfile? profile = await context.CompanyProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == query.ProfileId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<AdminCompanyPageDetail>(
                Error.NotFound("CompanyPage.NotFound", "Profilul firmei nu a fost găsit."));
        }

        User? owner = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == profile.UserId, cancellationToken);

        int carCount = await context.Cars
            .AsNoTracking()
            .Where(c => c.PostedByUserId == profile.UserId)
            .Where(CarVisibility.IsPublic)
            .CountAsync(cancellationToken);

        return Result.Success(Map(profile, owner?.Email ?? string.Empty, carCount));
    }

    internal static AdminCompanyPageDetail Map(CompanyProfile profile, string ownerEmail, int carCount) => new(
        profile.Id,
        profile.UserId,
        profile.LegalName,
        profile.Slug,
        profile.OwnerType.ToString(),
        profile.Cui,
        ownerEmail,
        profile.Phone,
        profile.Email,
        profile.Website,
        profile.LogoUrl,
        CompanyProfileMapper.ToModerationDto(profile),
        new AdminCompanyPageVersion(
            profile.Tagline,
            profile.PublicDescription,
            profile.CoverImageUrl,
            profile.PageTheme,
            profile.PageContent,
            new PickupLocationDto(
                profile.PickupAddress,
                profile.PickupLatitude,
                profile.PickupLongitude,
                profile.PickupNote)),
        profile.PublishedPage.ApprovedAtUtc.HasValue
            ? new AdminCompanyPageVersion(
                profile.PublishedPage.Tagline,
                profile.PublishedPage.PublicDescription,
                profile.PublishedPage.CoverImageUrl,
                profile.PublishedPage.Theme,
                profile.PublishedPage.Content,
                new PickupLocationDto(
                    profile.PublishedPage.PickupAddress,
                    profile.PublishedPage.PickupLatitude,
                    profile.PublishedPage.PickupLongitude,
                    profile.PublishedPage.PickupNote))
            : null,
        carCount);
}
