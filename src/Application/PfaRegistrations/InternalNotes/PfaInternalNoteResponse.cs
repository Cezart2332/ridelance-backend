using System;

namespace Application.PfaRegistrations.InternalNotes;

public sealed record PfaInternalNoteResponse(
    Guid Id,
    Guid PfaRegistrationId,
    int Year,
    int Month,
    string Content,
    Guid CreatedByUserId,
    string CreatedByUserName,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
