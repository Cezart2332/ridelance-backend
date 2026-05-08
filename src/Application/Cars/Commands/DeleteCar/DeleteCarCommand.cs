using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Commands.DeleteCar;

public sealed record DeleteCarCommand(Guid CarId) : ICommand;

internal sealed class DeleteCarCommandHandler(IApplicationDbContext context)
    : ICommandHandler<DeleteCarCommand>
{
    public async Task<Result> Handle(DeleteCarCommand command, CancellationToken cancellationToken)
    {
        Car? car = await context.Cars
            .FirstOrDefaultAsync(c => c.Id == command.CarId, cancellationToken);

        if (car is null)
        {
            return Result.Failure(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        context.Cars.Remove(car);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
