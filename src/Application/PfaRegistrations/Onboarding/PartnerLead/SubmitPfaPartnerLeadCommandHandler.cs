using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.PartnerLead;

internal sealed class SubmitPfaPartnerLeadCommandHandler(
    IApplicationDbContext context,
    OnboardingStateService stateService)
    : ICommandHandler<SubmitPfaPartnerLeadCommand, PfaPartnerLeadResponse>
{
    private static readonly Error ConsentRequired = Error.Failure(
        "Onboarding.PartnerLead.ConsentRequired",
        "Este necesar acordul de transmitere a datelor către partener.");

    private static readonly Error NoRegistration = Error.Problem(
        "Onboarding.PartnerLead.NoRegistration",
        "Nu există un dosar PFA de tip „Nu am PFA” pentru care să trimitem cererea către partener.");

    public async Task<Result<PfaPartnerLeadResponse>> Handle(
        SubmitPfaPartnerLeadCommand command,
        CancellationToken cancellationToken)
    {
        // Poarta RL-01: se scrie doar pe pasul activ. Prima verificare din handler —
        // altfel am valida conținutul unei cereri care oricum nu are voie să treacă.
        Result guard = await stateService.EnsureWritableAsync(
            command.UserId, OnboardingStepKey.Pfa, cancellationToken);

        if (guard.IsFailure)
        {
            return Result.Failure<PfaPartnerLeadResponse>(guard.Error);
        }

        if (!command.DataSharingConsent)
        {
            return Result.Failure<PfaPartnerLeadResponse>(ConsentRequired);
        }

        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.PartnerLead)
            .Where(r => r.UserId == command.UserId && r.RegistrationType == RegistrationType.NuAmPfa)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Failure<PfaPartnerLeadResponse>(NoRegistration);
        }

        DateTime nowUtc = DateTime.UtcNow;
        registration.PfaSource = PfaSource.ViaPartner;

        PfaPartnerLead lead = registration.PartnerLead ?? new PfaPartnerLead
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            CreatedAtUtc = nowUtc,
        };

        if (registration.PartnerLead is null)
        {
            context.PfaPartnerLeads.Add(lead);
        }

        lead.Phone = command.Phone;
        lead.Email = command.Email;
        lead.County = command.County;
        lead.HousingType = command.HousingType;
        lead.DataSharingConsent = true;
        lead.DataSharingConsentAtUtc ??= nowUtc;
        lead.UpdatedAtUtc = nowUtc;
        if (registration.PartnerLead is null)
        {
            lead.Status = PfaPartnerLeadStatus.RequestSent;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(ToResponse(lead));
    }

    internal static PfaPartnerLeadResponse ToResponse(PfaPartnerLead lead) => new(
        lead.Id,
        lead.PfaRegistrationId,
        lead.Provider,
        lead.Phone,
        lead.Email,
        lead.County,
        lead.HousingType,
        lead.DataSharingConsent,
        lead.Status.ToString(),
        lead.AdminNote);
}
