using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Eligibility;

internal sealed class GetEligibilityProfileQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetEligibilityProfileQuery, EligibilityProfileResponse?>
{
    public async Task<Result<EligibilityProfileResponse?>> Handle(
        GetEligibilityProfileQuery query,
        CancellationToken cancellationToken)
    {
        OnboardingEligibilityProfile? profile = await context.OnboardingEligibilityProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == query.UserId, cancellationToken);

        if (profile is null)
        {
            return Result.Success<EligibilityProfileResponse?>(null);
        }

        IReadOnlyList<string> reasons = string.IsNullOrEmpty(profile.StatusReason)
            ? []
            : profile.StatusReason.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        return Result.Success<EligibilityProfileResponse?>(new EligibilityProfileResponse(
            profile.Id,
            profile.DateOfBirth,
            profile.IdSeriesMask,
            profile.CategoryBObtainedOn,
            profile.DrivingCategories,
            profile.DrivingLicenceExpiresOn,
            profile.HasDriverCertificate,
            profile.DriverCertificateExpiresOn,
            profile.Status.ToString(),
            reasons));
    }
}
