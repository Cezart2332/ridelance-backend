using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.FiscalProfile;

internal sealed class GetPfaFiscalProfileQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetPfaFiscalProfileQuery, PfaFiscalSettingsResponse>
{
    public async Task<Result<PfaFiscalSettingsResponse>> Handle(
        GetPfaFiscalProfileQuery query,
        CancellationToken cancellationToken)
    {
        Result<PfaRegistration> access = await PfaAccess.EnsureCanViewAsync(
            context,
            userContext,
            query.PfaRegistrationId,
            cancellationToken);

        if (access.IsFailure)
        {
            return Result.Failure<PfaFiscalSettingsResponse>(access.Error);
        }

        PfaFiscalProfile? profile = await context.PfaFiscalProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.PfaRegistrationId == query.PfaRegistrationId, cancellationToken);

        List<PfaPlatformAccount> accountEntities = await context.PfaPlatformAccounts
            .AsNoTracking()
            .Where(a => a.PfaRegistrationId == query.PfaRegistrationId)
            .OrderBy(a => a.Kind)
            .ThenBy(a => a.Provider)
            .ToListAsync(cancellationToken);

        PfaFleetConsent? consent = await context.PfaFleetConsents
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.PfaRegistrationId == query.PfaRegistrationId, cancellationToken);

        return new PfaFiscalSettingsResponse(
            profile is null
                ? PfaFiscalProfileMapper.DefaultProfile(query.PfaRegistrationId)
                : PfaFiscalProfileMapper.MapProfile(profile),
            accountEntities.Select(PfaFiscalProfileMapper.MapAccount).ToList(),
            consent is null
                ? PfaFiscalProfileMapper.DefaultConsent(query.PfaRegistrationId)
                : PfaFiscalProfileMapper.MapConsent(consent));
    }
}
