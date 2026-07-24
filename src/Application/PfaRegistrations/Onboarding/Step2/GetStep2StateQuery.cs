using Application.Abstractions.Messaging;

namespace Application.PfaRegistrations.Onboarding.Step2;

public sealed record GetStep2StateQuery(Guid UserId) : IQuery<Step2StateResponse>;
