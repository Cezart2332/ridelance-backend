using Application.Abstractions.Messaging;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.GetState;

internal sealed class GetOnboardingStateQueryHandler(
    OnboardingStateService stateService,
    DevTools.OnboardingDevToolsGate devToolsGate,
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

        // Poarta uneltelor de dezvoltare, evaluată pe server. UI-ul doar o citește: el nu are
        // cum să decidă singur, iar dacă ar decide, endpoint-urile tot ar refuza (spec §13.1).
        bool devToolsEnabled = await devToolsGate.IsAllowedAsync(query.UserId, cancellationToken);

        return Result.Success(state with
        {
            TestSkipEnabled = testSkipEnabled,
            DevToolsEnabled = devToolsEnabled,
        });
    }
}
