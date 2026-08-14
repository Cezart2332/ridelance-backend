using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Commands.RecordCarClick;

/// <summary>Apăsarea pe CTA-ul unui anunț. Rămâne un simplu contor: nu se cere „click-uri unice”.</summary>
public sealed record RecordCarClickCommand(Guid CarId) : ICommand;

internal sealed class RecordCarClickCommandHandler(IApplicationDbContext context)
    : ICommandHandler<RecordCarClickCommand>
{
    public async Task<Result> Handle(RecordCarClickCommand command, CancellationToken cancellationToken)
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

        await context.Cars
            .Where(c => c.Id == command.CarId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.ClickCount, c => c.ClickCount + 1),
                cancellationToken);

        return Result.Success();
    }
}
