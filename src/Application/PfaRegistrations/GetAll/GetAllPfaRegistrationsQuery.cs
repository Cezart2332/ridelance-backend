using Application.Abstractions.Messaging;

namespace Application.PfaRegistrations.GetAll;

public sealed record GetAllPfaRegistrationsQuery(int Page = 1, int PageSize = 20)
    : IQuery<PfaRegistrationListResponse>;

public sealed record PfaRegistrationListResponse(
    List<PfaRegistrationSummary> Items,
    int TotalCount);

public sealed record PfaRegistrationSummary(
    Guid Id,
    Guid UserId,
    string UserEmail,
    string UserName,
    string RegistrationType,
    string Status,
    int DocumentCount,
    DateTime CreatedAtUtc,
    DateTime? LastActivityAtUtc);
