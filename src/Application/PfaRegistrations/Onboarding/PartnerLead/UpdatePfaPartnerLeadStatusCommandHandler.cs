using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.PartnerLead;

internal sealed class UpdatePfaPartnerLeadStatusCommandHandler(IApplicationDbContext context)
    : ICommandHandler<UpdatePfaPartnerLeadStatusCommand, PfaPartnerLeadResponse>
{
    private static readonly Error NotFound = Error.NotFound(
        "Onboarding.PartnerLead.NotFound",
        "Nu există un lead către partener pentru acest dosar.");

    public async Task<Result<PfaPartnerLeadResponse>> Handle(
        UpdatePfaPartnerLeadStatusCommand command,
        CancellationToken cancellationToken)
    {
        PfaPartnerLead? lead = await context.PfaPartnerLeads
            .SingleOrDefaultAsync(l => l.PfaRegistrationId == command.RegistrationId, cancellationToken);

        if (lead is null)
        {
            return Result.Failure<PfaPartnerLeadResponse>(NotFound);
        }

        lead.Status = command.Status;
        lead.AdminNote = command.AdminNote;
        lead.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(SubmitPfaPartnerLeadCommandHandler.ToResponse(lead));
    }
}
