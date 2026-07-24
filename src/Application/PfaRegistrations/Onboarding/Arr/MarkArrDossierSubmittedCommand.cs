using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Arr;

/// <summary>Pasul 3 — clientul confirmă că a depus dosarul la ARR („Am depus dosarul").</summary>
public sealed record MarkArrDossierSubmittedCommand(Guid UserId) : ICommand;

internal sealed class MarkArrDossierSubmittedCommandHandler(IApplicationDbContext context)
    : ICommandHandler<MarkArrDossierSubmittedCommand>
{
    public async Task<Result> Handle(MarkArrDossierSubmittedCommand command, CancellationToken cancellationToken)
    {
        ArrAuthorizationRequest? request = await context.ArrAuthorizationRequests
            .Where(a => a.PfaRegistration.UserId == command.UserId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (request is null)
        {
            return Result.Failure(ArrShared.NotFound);
        }

        request.Status = ArrAuthorizationStatus.Submitted;
        request.SubmittedAtUtc ??= DateTime.UtcNow;
        request.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
