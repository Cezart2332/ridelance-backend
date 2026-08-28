using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Application.Cars;

namespace Application.Cars.Commands.SubmitCarLead;

public sealed record SubmitCarLeadCommand(
    Guid CarId,
    string UserName,
    string UserEmail,
    string UserPhone,
    string City,
    string InterestType,
    bool ConsentAccepted,
    string? Intent = null,
    DateOnly? PreferredStartDate = null,
    int? Weeks = null,
    bool? HasPlatformAccount = null,
    string? Message = null,
    string? Source = null) : ICommand<Guid>;

internal sealed class SubmitCarLeadCommandHandler(IApplicationDbContext context)
    : ICommandHandler<SubmitCarLeadCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SubmitCarLeadCommand command, CancellationToken cancellationToken)
    {
        // Acordul e condiție de stocare, nu o bifă de formular: fără el n-avem dreptul să păstrăm
        // datele, deci refuzăm înainte de orice scriere.
        if (!command.ConsentAccepted)
        {
            return Result.Failure<Guid>(Error.Problem(
                "CarLead.ConsentRequired",
                "Trebuie să accepți prelucrarea datelor pentru a trimite cererea."));
        }

        Car? car = await context.Cars
            .Where(c => c.Id == command.CarId)
            .Where(CarVisibility.IsPublic)
            .FirstOrDefaultAsync(cancellationToken);

        if (car is null)
        {
            return Result.Failure<Guid>(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        if (!Enum.TryParse(command.Intent, out CarLeadIntent intent))
        {
            intent = CarLeadIntent.Request;
        }

        var lead = new CarLead
        {
            Id = Guid.NewGuid(),
            CarId = command.CarId,
            CarName = $"{car.Brand} {car.Model}",
            UserName = command.UserName,
            UserEmail = command.UserEmail,
            UserPhone = command.UserPhone,
            City = command.City,
            InterestType = command.InterestType,
            Intent = intent,
            PreferredStartDate = command.PreferredStartDate,
            Weeks = command.Weeks,
            HasPlatformAccount = command.HasPlatformAccount,
            Message = string.IsNullOrWhiteSpace(command.Message) ? null : command.Message.Trim(),
            Source = TrafficSource.Normalize(command.Source),
            Status = CarLeadStatus.New,
            ConsentAcceptedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.CarLeads.Add(lead);
        await context.SaveChangesAsync(cancellationToken);

        return lead.Id;
    }
}
