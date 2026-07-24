using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Vehicle;

/// <summary>Pasul 5 — adminul înregistrează copia conformă emisă (număr, valabilitate, document).</summary>
public sealed record RecordCopyConformaCommand(
    Guid RegistrationId,
    Guid? CopyConformaDocumentId,
    string? CopyConformaNumber,
    DateOnly? IssuedOn,
    DateOnly? ExpiresOn,
    string? AdminNote) : ICommand;

internal sealed class RecordCopyConformaCommandHandler(IApplicationDbContext context)
    : ICommandHandler<RecordCopyConformaCommand>
{
    public async Task<Result> Handle(RecordCopyConformaCommand command, CancellationToken cancellationToken)
    {
        VehicleCopyRequest? copy = await context.VehicleCopyRequests
            .Include(c => c.Vehicle)
            .Where(c => c.Vehicle.PfaRegistrationId == command.RegistrationId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (copy is null)
        {
            return Result.Failure(VehicleShared.CopyRequestNotFound);
        }

        DateTime nowUtc = DateTime.UtcNow;
        copy.CopyConformaDocumentId = command.CopyConformaDocumentId;
        copy.CopyConformaNumber = command.CopyConformaNumber;
        copy.IssuedOn = command.IssuedOn;
        copy.ExpiresOn = command.ExpiresOn;
        copy.AdminNote = command.AdminNote;
        copy.Status = VehicleCopyRequestStatus.Issued;
        copy.UpdatedAtUtc = nowUtc;

        copy.Vehicle.Status = PfaVehicleStatus.Active;
        copy.Vehicle.UpdatedAtUtc = nowUtc;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
