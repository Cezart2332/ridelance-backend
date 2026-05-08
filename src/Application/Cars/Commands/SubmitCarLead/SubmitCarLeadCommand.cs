using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Commands.SubmitCarLead;

public sealed record SubmitCarLeadCommand(
    Guid CarId,
    string UserName,
    string UserEmail,
    string UserPhone,
    string InterestType) : ICommand<Guid>;

internal sealed class SubmitCarLeadCommandHandler(IApplicationDbContext context)
    : ICommandHandler<SubmitCarLeadCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SubmitCarLeadCommand command, CancellationToken cancellationToken)
    {
        Car? car = await context.Cars
            .FirstOrDefaultAsync(c => c.Id == command.CarId && c.Active, cancellationToken);

        if (car is null)
        {
            return Result.Failure<Guid>(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        var lead = new CarLead
        {
            Id = Guid.NewGuid(),
            CarId = command.CarId,
            CarName = $"{car.Brand} {car.Model}",
            UserName = command.UserName,
            UserEmail = command.UserEmail,
            UserPhone = command.UserPhone,
            InterestType = command.InterestType,
            Status = CarLeadStatus.New,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.CarLeads.Add(lead);
        await context.SaveChangesAsync(cancellationToken);

        return lead.Id;
    }
}
