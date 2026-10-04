using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.RefreshToken;

internal sealed class RefreshTokenCommandHandler(
    IApplicationDbContext context,
    ITokenProvider tokenProvider) : ICommandHandler<RefreshTokenCommand, RefreshTokenResponse>
{
    public async Task<Result<RefreshTokenResponse>> Handle(
        RefreshTokenCommand command,
        CancellationToken cancellationToken)
    {
        User? user = await context.Users
            .SingleOrDefaultAsync(u => u.RefreshToken == command.RefreshToken, cancellationToken);

        // Un cont închis nu-și mai reînnoiește sesiunea, chiar dacă avea un token valid la închidere.
        // Un cont de echipă fără 2FA nu-și prelungește sesiunea: trebuie să treacă prin login și să-l configureze.
        if (user is null || user.RefreshTokenExpiryUtc < DateTime.UtcNow || user.IsDeleted || user.IsStaff && !user.IsTwoFactorEnabled)
        {
            return Result.Failure<RefreshTokenResponse>(UserErrors.InvalidRefreshToken);
        }

        string newAccessToken = tokenProvider.Create(user);
        string newRefreshToken = tokenProvider.CreateRefreshToken();

        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiryUtc = DateTime.UtcNow.AddDays(7);
        user.LastActivityAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return new RefreshTokenResponse(newAccessToken, newRefreshToken, user.Role.ToString(), user.Id);
    }
}
