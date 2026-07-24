using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Arr;

/// <summary>Pasul 3 — se înregistrează autorizația ARR emisă (număr, valabilitate, document).</summary>
public sealed record RecordArrAuthorizationCommand(
    Guid RegistrationId,
    Guid? AuthorizationDocumentId,
    string? AuthorizationNumber,
    DateOnly? AuthorizationIssuedOn,
    DateOnly? AuthorizationExpiresOn,
    string? AdminNote) : ICommand;

internal sealed class RecordArrAuthorizationCommandHandler(IApplicationDbContext context)
    : ICommandHandler<RecordArrAuthorizationCommand>
{
    public async Task<Result> Handle(RecordArrAuthorizationCommand command, CancellationToken cancellationToken)
    {
        ArrAuthorizationRequest? request = await context.ArrAuthorizationRequests
            .SingleOrDefaultAsync(a => a.PfaRegistrationId == command.RegistrationId, cancellationToken);

        if (request is null)
        {
            return Result.Failure(ArrShared.NotFound);
        }

        request.AuthorizationDocumentId = command.AuthorizationDocumentId;
        request.AuthorizationNumber = command.AuthorizationNumber;
        request.AuthorizationIssuedOn = command.AuthorizationIssuedOn;
        request.AuthorizationExpiresOn = command.AuthorizationExpiresOn;
        request.AdminNote = command.AdminNote;
        request.Status = ArrAuthorizationStatus.Issued;
        request.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
