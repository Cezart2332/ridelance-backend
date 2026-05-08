using Application.Abstractions.Messaging;

namespace Application.Users.RefreshToken;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<RefreshTokenResponse>;

public sealed record RefreshTokenResponse(
    string AccessToken,
    string RefreshToken,
    string Role,
    Guid UserId);
