using Application.Abstractions.Messaging;

namespace Application.Users.Login;

public sealed record LoginUserCommand(string Email, string Password) : ICommand<LoginResponse>;

public sealed record LoginResponse(string AccessToken, string RefreshToken, string Role, Guid UserId);
