using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Security;
using Application.Abstractions.Services;
using Domain.Banking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.Banking.Commands;

/// <param name="InstitutionId">Banca preselectată, dacă utilizatorul a ales una.</param>
public sealed record InitiateBankConnectionCommand(string? InstitutionId)
    : ICommand<InitiateBankConnectionResponse>;

/// <param name="ExpiresAtUtc">Linkul e de unică folosință și expiră — pagina oprește așteptarea la el.</param>
public sealed record InitiateBankConnectionResponse(string Link, DateTime? ExpiresAtUtc);

/// <summary>
/// Mintează linkul de conectare și pregătește revendicarea.
///
/// Pasul care contează e snapshotul: înainte de a trimite utilizatorul la provider, notăm ce
/// conexiuni existau deja. Fără el, după conectare n-am avea cum să distingem conexiunea lui de
/// ale celorlalți clienți din același cont de provider.
/// </summary>
internal sealed class InitiateBankConnectionCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IBankDataProvider provider,
    IConfiguration configuration)
    : ICommandHandler<InitiateBankConnectionCommand, InitiateBankConnectionResponse>
{
    public async Task<Result<InitiateBankConnectionResponse>> Handle(
        InitiateBankConnectionCommand command,
        CancellationToken cancellationToken)
    {
        if (!provider.IsConfigured)
        {
            return Result.Failure<InitiateBankConnectionResponse>(BankErrors.NotConfigured);
        }

        Guid userId = userContext.UserId;

        BankConnection? connection = await context.BankConnections
            .FirstOrDefaultAsync(bc => bc.UserId == userId, cancellationToken);

        if (connection?.Status == BankConnectionStatus.Linked)
        {
            return Result.Failure<InitiateBankConnectionResponse>(BankErrors.AlreadyLinked);
        }

        // Snapshotul se ia înaintea mintării, ca fereastra în care poate apărea o conexiune
        // străină să fie cât mai îngustă.
        IReadOnlyList<BankProviderConnection> before = await provider.ListConnectionsAsync(cancellationToken);

        BankLinkCreated link;
        try
        {
            link = await provider.MintConnectionLinkAsync(command.InstitutionId, cancellationToken);
        }
        catch (BankDataProviderException ex)
        {
            return Result.Failure<InitiateBankConnectionResponse>(
                Error.Problem("Bank.LinkFailed", ex.Message));
        }

        // Fereastra de istoric la prima sincronizare. Fintable nu expune o limită per bancă,
        // așa cum făcea PSD2, deci e o singură valoare pentru toți.
        int historyDays = int.TryParse(configuration["Fintable:InitialHistoryDays"], out int configured)
            ? configured
            : 365;

        if (connection is null)
        {
            connection = new BankConnection
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Provider = provider.ProviderName,
                CreatedAtUtc = DateTime.UtcNow,
            };
            context.BankConnections.Add(connection);
        }

        connection.Provider = provider.ProviderName;
        connection.InstitutionId = command.InstitutionId ?? string.Empty;
        connection.InstitutionName = string.Empty;
        connection.Reference = Guid.NewGuid().ToString("N");
        connection.Status = BankConnectionStatus.Created;
        connection.LinkExpiresAtUtc = link.ExpiresAtUtc;
        connection.KnownConnectionIdsJson = BankConnectionClaimService.SerializeKnown(before.Select(c => c.Id));
        connection.MaxHistoricalDays = historyDays;
        connection.ErrorMessage = null;
        connection.ConsecutiveFailures = 0;

        await context.SaveChangesAsync(cancellationToken);

        return new InitiateBankConnectionResponse(link.Address, link.ExpiresAtUtc);
    }
}
