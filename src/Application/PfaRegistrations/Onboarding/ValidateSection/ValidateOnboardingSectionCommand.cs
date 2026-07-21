using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding.ValidateSection;

public sealed record ValidateOnboardingSectionCommand(
    Guid RegistrationId,
    OnboardingSectionKey SectionKey,
    Guid ReviewerUserId) : ICommand;
