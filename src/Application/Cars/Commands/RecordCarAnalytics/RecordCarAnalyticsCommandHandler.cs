using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Commands.RecordCarAnalytics;

internal sealed class RecordCarAnalyticsCommandHandler(IApplicationDbContext context)
    : ICommandHandler<RecordCarAnalyticsCommand>
{
    public async Task<Result> Handle(RecordCarAnalyticsCommand command, CancellationToken cancellationToken)
    {
        bool carExists = await context.Cars
            .AsNoTracking()
            .AnyAsync(
                c => c.Id == command.CarId &&
                     c.Active &&
                     c.ApprovalStatus == CarApprovalStatus.Approved,
                cancellationToken);

        if (!carExists)
        {
            return Result.Failure(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        if (command.EventType == CarAnalyticsEventType.View)
        {
            await context.Cars
                .Where(c => c.Id == command.CarId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(c => c.ViewCount, c => c.ViewCount + 1),
                    cancellationToken);
        }
        else
        {
            await context.Cars
                .Where(c => c.Id == command.CarId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(c => c.ClickCount, c => c.ClickCount + 1),
                    cancellationToken);
        }

        return Result.Success();
    }
}
