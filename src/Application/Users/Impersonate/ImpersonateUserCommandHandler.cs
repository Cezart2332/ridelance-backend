using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Users.Login;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.Impersonate;

internal sealed class ImpersonateUserCommandHandler(
    IApplicationDbContext context,
    ITokenProvider tokenProvider,
    IUserContext userContext) : ICommandHandler<ImpersonateUserCommand, LoginResponse>
{
    public async Task<Result<LoginResponse>> Handle(ImpersonateUserCommand command, CancellationToken cancellationToken)
    {
        // Check if the caller is an Admin
        User? caller = await context.Users
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (caller is null || caller.Role != UserRole.Admin)
        {
            return Result.Failure<LoginResponse>(UserErrors.Unauthorized());
        }

        // Find the target user
        User? targetUser = await context.Users
            .SingleOrDefaultAsync(u => u.Id == command.TargetUserId, cancellationToken);

        if (targetUser is null)
        {
            return Result.Failure<LoginResponse>(UserErrors.NotFound(command.TargetUserId));
        }

        // Generate only a short-lived access token for the target user.
        // The refresh token (cookie) stays the admin's, so the admin session
        // survives and the target user's own devices are not logged out.
        string accessToken = tokenProvider.Create(targetUser);

        targetUser.LastActivityAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        var response = new LoginResponse(
            accessToken,
            string.Empty,
            targetUser.Role.ToString(),
            targetUser.Id);

        return response;
    }
}
