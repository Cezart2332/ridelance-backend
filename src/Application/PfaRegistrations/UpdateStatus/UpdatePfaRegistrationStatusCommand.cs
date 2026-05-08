using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.UpdateStatus;

public sealed record UpdatePfaRegistrationStatusCommand(
    Guid RegistrationId,
    Guid ReviewerUserId,
    PfaRegistrationStatus NewStatus,
    string? ReviewNote,
    string? Cui = null,
    Guid? DocumentId = null) : ICommand;
