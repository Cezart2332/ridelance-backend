using Application.Abstractions.Messaging;

namespace Application.Users.Logout;

public sealed record LogoutCommand(Guid UserId) : ICommand;
