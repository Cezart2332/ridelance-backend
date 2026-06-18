using Application.Abstractions.Messaging;

namespace Application.Payments.GetSessionStatus;

public sealed record GetSessionStatusQuery(string SessionId) : IQuery<SessionStatusResponse>;

public sealed record SessionStatusResponse(string Status, string? CustomerEmail);
