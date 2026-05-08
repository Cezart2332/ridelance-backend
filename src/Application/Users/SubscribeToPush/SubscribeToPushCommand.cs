using Application.Abstractions.Messaging;

namespace Application.Users.SubscribeToPush;

public sealed record SubscribeToPushCommand(
    Guid UserId,
    string Endpoint,
    string P256dh,
    string Auth) : ICommand;
