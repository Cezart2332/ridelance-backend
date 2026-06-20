using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.FiscalProfile;

internal sealed class AcceptPfaFleetConsentCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<AcceptPfaFleetConsentCommand, PfaFleetConsentResponse>
{
    public async Task<Result<PfaFleetConsentResponse>> Handle(
        AcceptPfaFleetConsentCommand command,
        CancellationToken cancellationToken)
    {
        Result<PfaRegistration> access = await PfaAccess.EnsureCanViewAsync(
            context,
            userContext,
            command.PfaRegistrationId,
            cancellationToken);

        if (access.IsFailure)
        {
            return Result.Failure<PfaFleetConsentResponse>(access.Error);
        }

        if (access.Value.UserId != userContext.UserId)
        {
            return Result.Failure<PfaFleetConsentResponse>(
                Error.Failure("PfaFleetConsent.Forbidden", "Only the PFA owner can accept fleet permissions."));
        }

        PfaFleetConsent? consent = await context.PfaFleetConsents
            .SingleOrDefaultAsync(c => c.PfaRegistrationId == command.PfaRegistrationId, cancellationToken);

        if (consent is null)
        {
            consent = new PfaFleetConsent
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = command.PfaRegistrationId
            };
            context.PfaFleetConsents.Add(consent);
        }

        if (command.FleetAccountsAccepted && !consent.FleetAccountsAccepted)
        {
            consent.FleetAccountsAccepted = true;
            consent.FleetAccountsAcceptedAtUtc = DateTime.UtcNow;
        }

        if (command.BoltApiAccepted && !consent.BoltApiAccepted)
        {
            consent.BoltApiAccepted = true;
            consent.BoltApiAcceptedAtUtc = DateTime.UtcNow;
        }

        consent.AcceptedByUserId = userContext.UserId;
        consent.ConsentTextVersion = "2026-06";

        await context.SaveChangesAsync(cancellationToken);

        return PfaFiscalProfileMapper.MapConsent(consent);
    }
}
