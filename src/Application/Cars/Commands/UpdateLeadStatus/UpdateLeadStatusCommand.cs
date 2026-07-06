using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Commands.UpdateLeadStatus;

public sealed record UpdateLeadStatusCommand(Guid LeadId, string Status, string? AdminNote) : ICommand;

internal sealed class UpdateLeadStatusCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<UpdateLeadStatusCommand>
{
    public async Task<Result> Handle(UpdateLeadStatusCommand command, CancellationToken cancellationToken)
    {
        CarLead? lead = await context.CarLeads
            .FirstOrDefaultAsync(l => l.Id == command.LeadId, cancellationToken);

        if (lead is null)
        {
            return Result.Failure(Error.NotFound("CarLead.NotFound", "Lead-ul nu a fost găsit."));
        }

        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        bool isOwnerPoster = caller?.Role == UserRole.CarPoster
            && await context.Cars.AnyAsync(
                c => c.Id == lead.CarId && c.PostedByUserId == userContext.UserId,
                cancellationToken);

        if (caller?.Role != UserRole.Admin && !isOwnerPoster)
        {
            return Result.Failure(Error.Failure("CarLead.Forbidden", "Nu poți modifica acest lead."));
        }

        if (!Enum.TryParse<CarLeadStatus>(command.Status, out CarLeadStatus status))
        {
            return Result.Failure(Error.Problem("CarLead.InvalidStatus", "Status invalid."));
        }

        lead.Status = status;
        if (command.AdminNote is not null)
        {
            lead.AdminNote = command.AdminNote;
        }

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
