using Application.Abstractions.Messaging;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.GetState;

public sealed record GetOnboardingStateForRegistrationQuery(Guid RegistrationId) : IQuery<OnboardingStateResponse>;

internal sealed class GetOnboardingStateForRegistrationQueryHandler(OnboardingStateService stateService)
    : IQueryHandler<GetOnboardingStateForRegistrationQuery, OnboardingStateResponse>
{
    public Task<Result<OnboardingStateResponse>> Handle(
        GetOnboardingStateForRegistrationQuery query,
        CancellationToken cancellationToken) =>
        stateService.GetForRegistrationAsync(query.RegistrationId, cancellationToken);
}
