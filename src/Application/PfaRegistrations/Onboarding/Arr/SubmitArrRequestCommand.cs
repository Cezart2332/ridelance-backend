using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Settings;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Arr;

/// <summary>Pasul 3 — clientul inițiază cererea de autorizație ARR (agenție + metodă de depunere).</summary>
public sealed record SubmitArrRequestCommand(
    Guid UserId,
    string? AgencyName,
    ArrSubmissionMethod SubmissionMethod) : ICommand<ArrStateResponse>;

internal sealed class SubmitArrRequestCommandHandler(
    IApplicationDbContext context,
    IAppSettings appSettings,
    OnboardingStateService stateService)
    : ICommandHandler<SubmitArrRequestCommand, ArrStateResponse>
{
    public async Task<Result<ArrStateResponse>> Handle(
        SubmitArrRequestCommand command,
        CancellationToken cancellationToken)
    {
        // Poarta RL-01: se scrie doar pe pasul activ. Prima verificare din handler —
        // altfel am valida conținutul unei cereri care oricum nu are voie să treacă.
        Result guard = await stateService.EnsureWritableAsync(
            command.UserId, OnboardingStepKey.Arr, cancellationToken);

        if (guard.IsFailure)
        {
            return Result.Failure<ArrStateResponse>(guard.Error);
        }

        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.ArrAuthorizationRequest)
            .Where(r => r.UserId == command.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Failure<ArrStateResponse>(ArrShared.NoRegistration);
        }

        ArrAuthorizationRequest request = await ArrShared.EnsureRequestAsync(
            context, appSettings, registration, cancellationToken);

        request.AgencyName = command.AgencyName;
        request.SubmissionMethod = command.SubmissionMethod;
        request.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(ArrShared.ToResponse(request));
    }
}
