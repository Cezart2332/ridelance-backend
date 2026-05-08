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

        if (user is null || user.RefreshTokenExpiryUtc < DateTime.UtcNow)
        {
            return Result.Failure<RefreshTokenResponse>(UserErrors.InvalidRefreshToken);
        }

        string newAccessToken = tokenProvider.Create(user);
        string newRefreshToken = tokenProvider.CreateRefreshToken();

        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiryUtc = DateTime.UtcNow.AddDays(7);

        await context.SaveChangesAsync(cancellationToken);

        return new RefreshTokenResponse(newAccessToken, newRefreshToken, user.Role.ToString(), user.Id);
    }
}
