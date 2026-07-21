using Application.Abstractions.Messaging;

namespace Application.PfaRegistrations.Onboarding.GetState;

public sealed record GetOnboardingStateQuery(Guid UserId) : IQuery<OnboardingStateResponse>;
