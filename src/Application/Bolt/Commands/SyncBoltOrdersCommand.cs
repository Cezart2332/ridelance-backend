using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Bolt;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Bolt.Commands;

public sealed record SyncBoltOrdersCommand : ICommand<bool>;

internal sealed class SyncBoltOrdersCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IBoltService boltService) : ICommandHandler<SyncBoltOrdersCommand, bool>
{
    public async Task<Result<bool>> Handle(
        SyncBoltOrdersCommand command,
        CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;

        BoltIntegration? integration = await context.BoltIntegrations
            .FirstOrDefaultAsync(bi => bi.UserId == userId, cancellationToken);

        if (integration == null)
        {
            return Result.Failure<bool>(Error.Failure("Bolt.NotConfigured", "Integrarea Bolt nu este configurată."));
        }

        try
        {
            DateTime end = DateTime.UtcNow;
            DateTime start = end.AddDays(-2); // Fetch orders from the last 2 days to account for late/delayed entries

            List<BoltOrder> orders = await boltService.FetchOrdersAsync(integration, start, end, cancellationToken);

            if (orders.Count > 0)
            {
                var existingRefs = orders.Select(o => o.OrderReference).ToList();
                List<BoltOrder> ordersToRemove = await context.BoltOrders
                    .Where(o => o.UserId == userId && existingRefs.Contains(o.OrderReference))
                    .ToListAsync(cancellationToken);

                context.BoltOrders.RemoveRange(ordersToRemove);

                foreach (BoltOrder order in orders)
                {
                    context.BoltOrders.Add(order);
                }
            }

            integration.IsConnected = true;
            integration.ErrorMessage = null;
            integration.LastFetchedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            integration.IsConnected = false;
            integration.ErrorMessage = ex.Message;
            await context.SaveChangesAsync(cancellationToken);

            return Result.Failure<bool>(Error.Failure("Bolt.SyncError", ex.Message));
        }
    }
}
