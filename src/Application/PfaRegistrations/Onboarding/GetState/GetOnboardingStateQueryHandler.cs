using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Payments;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.GetState;

internal sealed class GetOnboardingStateQueryHandler(
    IApplicationDbContext context,
    IConfiguration configuration)
    : IQueryHandler<GetOnboardingStateQuery, OnboardingStateResponse>
{
    public async Task<Result<OnboardingStateResponse>> Handle(
        GetOnboardingStateQuery query,
        CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .AsNoTracking()
            .Include(r => r.OnboardingSections)
            .Include(r => r.FiscalProfile)
            .Include(r => r.BankAccountDeclaration)
            .Include(r => r.OblioAccount)
            .Include(r => r.ArrAuthorizationRequest)
            .Include(r => r.PlatformAccounts)
            .Include(r => r.Vehicles).ThenInclude(v => v.CopyRequest)
            .Where(r => r.UserId == query.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        OnboardingEligibilityProfile? eligibility = await context.OnboardingEligibilityProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == query.UserId, cancellationToken);

        bool hasPaidInfiintare = await InfiintarePaymentCheck.HasPaidAsync(
            context, query.UserId, cancellationToken);

        // DOAR PENTRU TESTARE — de șters odată cu SkipOnboardingStepCommand.
        bool testSkipEnabled = bool.TryParse(configuration["Onboarding:EnableTestSkip"], out bool enabled) && enabled;

        return Result.Success(OnboardingStateBuilder.Build(registration, hasPaidInfiintare, eligibility) with
        {
            TestSkipEnabled = testSkipEnabled,
        });
    }
}
