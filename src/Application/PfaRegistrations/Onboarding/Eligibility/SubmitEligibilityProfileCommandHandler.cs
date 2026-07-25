using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Eligibility;

internal sealed class SubmitEligibilityProfileCommandHandler(IApplicationDbContext context)
    : ICommandHandler<SubmitEligibilityProfileCommand, EligibilityProfileResponse>
{
    public async Task<Result<EligibilityProfileResponse>> Handle(
        SubmitEligibilityProfileCommand command,
        CancellationToken cancellationToken)
    {
        OnboardingEligibilityProfile? profile = await context.OnboardingEligibilityProfiles
            .SingleOrDefaultAsync(p => p.UserId == command.UserId, cancellationToken);

        DateTime nowUtc = DateTime.UtcNow;

        if (profile is null)
        {
            profile = new OnboardingEligibilityProfile
            {
                Id = Guid.NewGuid(),
                UserId = command.UserId,
                CreatedAtUtc = nowUtc,
            };
            context.OnboardingEligibilityProfiles.Add(profile);
        }

        // Userul răspunde DOAR la întrebări; datele de pe documente vin din OCR și nu se șterg
        // dacă nu sunt trimise (document-first). Valorile explicite le suprascriu pe cele citite.
        profile.DateOfBirth = command.DateOfBirth ?? profile.DateOfBirth;
        profile.IdSeriesMask = command.IdSeriesMask ?? profile.IdSeriesMask;
        profile.CategoryBObtainedOn = command.CategoryBObtainedOn ?? profile.CategoryBObtainedOn;
        profile.DrivingCategories = command.DrivingCategories ?? profile.DrivingCategories;
        profile.DrivingLicenceExpiresOn = command.DrivingLicenceExpiresOn ?? profile.DrivingLicenceExpiresOn;
        profile.HasDriverCertificate = command.HasDriverCertificate;
        profile.DriverCertificateExpiresOn = command.DriverCertificateExpiresOn ?? profile.DriverCertificateExpiresOn;
        profile.UpdatedAtUtc = nowUtc;

        EligibilityEvaluation evaluation = EligibilityRules.Evaluate(
            profile.DateOfBirth,
            profile.CategoryBObtainedOn,
            profile.DrivingLicenceExpiresOn,
            profile.HasDriverCertificate,
            profile.DriverCertificateExpiresOn,
            DateOnly.FromDateTime(nowUtc));

        profile.Status = evaluation.Status;
        profile.StatusReason = evaluation.Reasons.Count == 0 ? null : string.Join('\n', evaluation.Reasons);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new EligibilityProfileResponse(
            profile.Id,
            profile.DateOfBirth,
            profile.IdSeriesMask,
            profile.CategoryBObtainedOn,
            profile.DrivingCategories,
            profile.DrivingLicenceExpiresOn,
            profile.HasDriverCertificate,
            profile.DriverCertificateExpiresOn,
            profile.Status.ToString(),
            evaluation.Reasons));
    }
}
