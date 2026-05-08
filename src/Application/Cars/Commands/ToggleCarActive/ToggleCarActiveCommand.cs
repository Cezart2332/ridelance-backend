using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Commands.ToggleCarActive;

public sealed record ToggleCarActiveCommand(Guid CarId) : ICommand<bool>;

internal sealed class ToggleCarActiveCommandHandler(IApplicationDbContext context)
    : ICommandHandler<ToggleCarActiveCommand, bool>
{
    public async Task<Result<bool>> Handle(ToggleCarActiveCommand command, CancellationToken cancellationToken)
    {
        Car? car = await context.Cars
            .FirstOrDefaultAsync(c => c.Id == command.CarId, cancellationToken);

        if (car is null)
        {
            return Result.Failure<bool>(Error.NotFound("Car.NotFound", "Masina nu a fost gasita."));
        }

        car.Active = !car.Active;
        car.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        return car.Active;
    }
}
