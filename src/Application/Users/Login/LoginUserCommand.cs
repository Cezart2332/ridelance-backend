using Application.Abstractions.Messaging;

namespace Application.Users.Login;

public sealed record LoginUserCommand(string Email, string Password) : ICommand<LoginResponse>;

/// <summary>
/// Sesiunea emisă la autentificare. Pentru echipă (Admin, Contabil) parola nu ajunge: răspunsul nu
/// are tokenuri, ci <see cref="TwoFactor"/> (<c>VERIFY</c> sau <c>SETUP</c>) și un
/// <see cref="ChallengeToken"/> de câteva minute pentru pasul 2FA.
/// </summary>
public sealed record LoginResponse(string AccessToken, string RefreshToken, string Role, Guid UserId, string? TwoFactor = null, string? ChallengeToken = null)
{
    public bool IsChallenge => ChallengeToken is not null;
}
