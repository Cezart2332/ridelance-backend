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
    IAppSettings appSettings)
    : ICommandHandler<SubmitArrRequestCommand, ArrStateResponse>
{
    public async Task<Result<ArrStateResponse>> Handle(
        SubmitArrRequestCommand command,
        CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.ArrAuthorizationRequest)
            .Where(r => r.UserId == command.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Failure<ArrStateResponse>(ArrShared.NoRegistration);
        }

        DateTime nowUtc = DateTime.UtcNow;

        ArrAuthorizationRequest request = registration.ArrAuthorizationRequest ?? new ArrAuthorizationRequest
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            CreatedAtUtc = nowUtc,
        };

        if (registration.ArrAuthorizationRequest is null)
        {
            context.ArrAuthorizationRequests.Add(request);
            // Snapshot-ul taxei se face o singură dată, la inițiere.
            request.FeeSnapshotBani = await appSettings.GetAsync(
                ArrShared.FeeSettingKey, ArrShared.DefaultFeeBani, cancellationToken);
        }

        request.AgencyName = command.AgencyName;
        request.SubmissionMethod = command.SubmissionMethod;
        request.UpdatedAtUtc = nowUtc;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(ArrShared.ToResponse(request));
    }
}
