using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Companies;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Companies.Queries.GetCompanyProfile;

/// <summary>Profilul firmei contului curent.</summary>
public sealed record GetCompanyProfileQuery : IQuery<CompanyProfileDto?>;

internal sealed class GetCompanyProfileQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetCompanyProfileQuery, CompanyProfileDto?>
{
    public async Task<Result<CompanyProfileDto?>> Handle(
        GetCompanyProfileQuery query,
        CancellationToken cancellationToken)
    {
        CompanyProfile? profile = await context.CompanyProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == userContext.UserId, cancellationToken);

        // Lipsa profilului nu e o eroare: e starea în care pornește orice cont nou. Frontendul
        // afișează formularul gol, nu un ecran de eroare.
        return Result.Success(profile is null ? null : CompanyProfileMapper.ToDto(profile));
    }
}
