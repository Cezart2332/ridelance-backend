using Application.Abstractions.Messaging;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.GetState;

internal sealed class GetOnboardingStateQueryHandler(
    OnboardingStateService stateService,
    IConfiguration configuration)
    : IQueryHandler<GetOnboardingStateQuery, OnboardingStateResponse>
{
    public async Task<Result<OnboardingStateResponse>> Handle(
        GetOnboardingStateQuery query,
        CancellationToken cancellationToken)
    {
        OnboardingStateResponse state = await stateService.GetForUserAsync(query.UserId, cancellationToken);

        // DOAR PENTRU TESTARE — de șters odată cu SkipOnboardingStepCommand.
        bool testSkipEnabled = bool.TryParse(configuration["Onboarding:EnableTestSkip"], out bool enabled) && enabled;

        return Result.Success(state with { TestSkipEnabled = testSkipEnabled });
    }
}
