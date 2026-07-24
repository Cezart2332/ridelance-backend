using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Platforms;

public sealed record GetPlatformOnboardingQuery(Guid UserId) : IQuery<PlatformOnboardingResponse>;

internal sealed class GetPlatformOnboardingQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetPlatformOnboardingQuery, PlatformOnboardingResponse>
{
    public async Task<Result<PlatformOnboardingResponse>> Handle(
        GetPlatformOnboardingQuery query,
        CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .AsNoTracking()
            .Include(r => r.PlatformAccounts)
            .Where(r => r.UserId == query.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Success(new PlatformOnboardingResponse(null, []));
        }

        return Result.Success(PlatformShared.ToResponse(registration));
    }
}
