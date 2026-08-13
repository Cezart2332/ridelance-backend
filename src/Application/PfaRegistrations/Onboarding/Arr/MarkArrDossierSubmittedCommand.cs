using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Arr;

/// <summary>Pasul 3 — clientul confirmă că a depus dosarul la ARR („Am depus dosarul").</summary>
public sealed record MarkArrDossierSubmittedCommand(Guid UserId) : ICommand;

internal sealed class MarkArrDossierSubmittedCommandHandler(
    IApplicationDbContext context,
    OnboardingStateService stateService)
    : ICommandHandler<MarkArrDossierSubmittedCommand>
{
    public async Task<Result> Handle(MarkArrDossierSubmittedCommand command, CancellationToken cancellationToken)
    {
        // Poarta RL-01: se scrie doar pe pasul activ. Prima verificare din handler —
        // altfel am valida conținutul unei cereri care oricum nu are voie să treacă.
        Result guard = await stateService.EnsureWritableAsync(
            command.UserId, OnboardingStepKey.Arr, cancellationToken);

        if (guard.IsFailure)
        {
            return Result.Failure(guard.Error);
        }

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
