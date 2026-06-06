using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Bolt;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Bolt.Commands;

public sealed record ConfigureBoltIntegrationCommand(
    string ClientId,
    string ClientSecret) : ICommand<Guid>;

internal sealed class ConfigureBoltIntegrationCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IBoltService boltService) : ICommandHandler<ConfigureBoltIntegrationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        ConfigureBoltIntegrationCommand command,
        CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;

        // Find or create integration
        BoltIntegration? integration = await context.BoltIntegrations
            .FirstOrDefaultAsync(bi => bi.UserId == userId, cancellationToken);

        bool isNew = false;
        if (integration == null)
        {
            isNew = true;
            integration = new BoltIntegration
            {
                Id = Guid.NewGuid(),
                UserId = userId
            };
        }

        integration.ClientId = command.ClientId.Trim();
        integration.ClientSecret = command.ClientSecret.Trim();
        integration.IsConnected = false;
        integration.ErrorMessage = null;

        try
        {
            // 1. Get access token (test credentials)
            string token = await boltService.GetAccessTokenAsync(integration, cancellationToken);

            // 2. Fetch company ID & Name
            (int companyId, string companyName) = await boltService.FetchCompanyIdAsync(token, cancellationToken);
            integration.CompanyId = companyId;
            integration.CompanyName = companyName;
            integration.IsConnected = true;

            // Save details so the service can query using companyId
            if (isNew)
            {
                context.BoltIntegrations.Add(integration);
            }
            await context.SaveChangesAsync(cancellationToken);

            // 3. Fetch orders for current month
            DateTime now = DateTime.UtcNow;
            var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime endOfMonth = startOfMonth.AddMonths(1).AddSeconds(-1);

            List<BoltOrder> orders = await boltService.FetchOrdersAsync(integration, startOfMonth, endOfMonth, cancellationToken);

            // 4. Save/Upsert orders
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

            integration.LastFetchedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);

            return integration.Id;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ConfigureBoltIntegration EXCEPTION: {ex}");
            integration.IsConnected = false;
            integration.ErrorMessage = ex.Message;
            
            if (isNew)
            {
                context.BoltIntegrations.Add(integration);
            }
            await context.SaveChangesAsync(cancellationToken);

            return Result.Failure<Guid>(Error.Failure("Bolt.ConfigError", ex.Message));
        }
    }
}
