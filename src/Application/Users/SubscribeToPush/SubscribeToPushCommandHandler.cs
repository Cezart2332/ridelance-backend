using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.SubscribeToPush;

internal sealed class SubscribeToPushCommandHandler(IApplicationDbContext context)
    : ICommandHandler<SubscribeToPushCommand>
{
    public async Task<Result> Handle(
        SubscribeToPushCommand request,
        CancellationToken cancellationToken)
    {
        // Check if user exists
        bool userExists = await context.Users.AnyAsync(u => u.Id == request.UserId, cancellationToken);
        if (!userExists)
        {
            return Result.Failure(UserErrors.NotFound(request.UserId));
        }

        // Add or update the subscription. Wait, we don't have PushSubscriptions in context yet.
        // I will add it to IApplicationDbContext soon.
        PushSubscription? existingSub = await context.PushSubscriptions
            .SingleOrDefaultAsync(s => s.Endpoint == request.Endpoint && s.UserId == request.UserId, cancellationToken);

        if (existingSub is null)
        {
            PushSubscription sub = new()
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                Endpoint = request.Endpoint,
                P256dh = request.P256dh,
                Auth = request.Auth,
                CreatedAtUtc = DateTime.UtcNow
            };
            context.PushSubscriptions.Add(sub);
        }
        else
        {
            existingSub.P256dh = request.P256dh;
            existingSub.Auth = request.Auth;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
