using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Platforms;

public sealed record GetPlatformOnboardingQuery(Guid UserId) : IQuery<PlatformOnboardingResponse>;

/// <summary>
/// Aceleași date, adresate prin dosar. Pentru admin: pasul 5 e singurul din onboarding fără
/// documente, deci fișa lui era goală în panoul de validare — adminul nu vedea nimic din ce
/// completase șoferul, deși totul era salvat.
/// </summary>
public sealed record GetPlatformOnboardingForRegistrationQuery(Guid PfaRegistrationId)
    : IQuery<PlatformOnboardingResponse>;

internal sealed class GetPlatformOnboardingQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetPlatformOnboardingQuery, PlatformOnboardingResponse>
{
    public async Task<Result<PlatformOnboardingResponse>> Handle(
        GetPlatformOnboardingQuery query,
        CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await PlatformShared.LoadAsync(
            context, r => r.UserId == query.UserId, cancellationToken);

        if (registration is null)
        {
            return Result.Success(new PlatformOnboardingResponse(null, []));
        }

        return Result.Success(PlatformShared.ToResponse(registration));
    }
}

internal sealed class GetPlatformOnboardingForRegistrationQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetPlatformOnboardingForRegistrationQuery, PlatformOnboardingResponse>
{
    public async Task<Result<PlatformOnboardingResponse>> Handle(
        GetPlatformOnboardingForRegistrationQuery query,
        CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await PlatformShared.LoadAsync(
            context, r => r.Id == query.PfaRegistrationId, cancellationToken);

        return registration is null
            ? Result.Failure<PlatformOnboardingResponse>(
                PfaRegistrationErrors.NotFound(query.PfaRegistrationId))
            : Result.Success(PlatformShared.ToResponse(registration));
    }
}
