using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.Logout;

internal sealed class LogoutCommandHandler(IApplicationDbContext context) : ICommandHandler<LogoutCommand>
{
    public async Task<Result> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        User? user = await context.Users
            .SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound(command.UserId));
        }

        user.RefreshToken = null;
        user.RefreshTokenExpiryUtc = null;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
