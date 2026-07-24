using Application.Abstractions.Messaging;

namespace Application.PfaRegistrations.Onboarding.Eligibility;

public sealed record GetEligibilityProfileQuery(Guid UserId) : IQuery<EligibilityProfileResponse?>;
