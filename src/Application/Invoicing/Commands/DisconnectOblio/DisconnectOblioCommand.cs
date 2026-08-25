using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Invoicing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Invoicing.Commands.DisconnectOblio;

public sealed record DisconnectOblioCommand : ICommand;

internal sealed class DisconnectOblioCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<DisconnectOblioCommand>
{
    public async Task<Result> Handle(DisconnectOblioCommand command, CancellationToken cancellationToken)
    {
        OblioIntegration? integration = await context.OblioIntegrations
            .FirstOrDefaultAsync(o => o.UserId == userContext.UserId, cancellationToken);

        if (integration is null)
        {
            return Result.Success();
        }

        // Rândul se șterge, nu se marchează deconectat: credențialele altcuiva n-au de ce să
        // rămână stocate, fie și criptate, după ce a cerut deconectarea.
        context.OblioIntegrations.Remove(integration);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
