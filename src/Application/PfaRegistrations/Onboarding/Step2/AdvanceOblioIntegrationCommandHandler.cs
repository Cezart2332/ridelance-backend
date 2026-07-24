using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Step2;

internal sealed class AdvanceOblioIntegrationCommandHandler(IApplicationDbContext context)
    : ICommandHandler<AdvanceOblioIntegrationCommand>
{
    public async Task<Result> Handle(AdvanceOblioIntegrationCommand command, CancellationToken cancellationToken)
    {
        PfaOblioAccount? account = await context.PfaOblioAccounts
            .SingleOrDefaultAsync(a => a.PfaRegistrationId == command.RegistrationId, cancellationToken);

        if (account is null)
        {
            return Result.Failure(Step2Errors.OblioNotFound);
        }

        account.IntegrationStatus = command.IntegrationStatus;
        account.AdminNote = command.AdminNote;
        account.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
