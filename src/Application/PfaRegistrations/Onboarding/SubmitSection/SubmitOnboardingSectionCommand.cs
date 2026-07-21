using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding.SubmitSection;

public sealed record SubmitOnboardingSectionCommand(
    Guid UserId,
    OnboardingSectionKey SectionKey) : ICommand;
