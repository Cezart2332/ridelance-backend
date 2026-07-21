using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding.RejectSection;

public sealed record RejectOnboardingSectionCommand(
    Guid RegistrationId,
    OnboardingSectionKey SectionKey,
    Guid ReviewerUserId,
    string Note) : ICommand;
