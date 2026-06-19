using System;

namespace Application.PfaRegistrations.InternalNotes;

public sealed record PfaActivityLogResponse(
    Guid Id,
    Guid PfaRegistrationId,
    string ActivityType,
    string Description,
    DateTime CreatedAtUtc,
    Guid PerformedByUserId,
    string PerformedByUserName);
