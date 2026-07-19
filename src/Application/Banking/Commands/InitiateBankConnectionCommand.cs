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

public sealed record InitiateBankConnectionResponse(string Link);

public sealed record InitiateBankConnectionCommand(string InstitutionId)
    : ICommand<InitiateBankConnectionResponse>;

internal sealed class InitiateBankConnectionCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IBankDataProvider provider,
    ISecretProtector secretProtector,
    IConfiguration configuration)
    : ICommandHandler<InitiateBankConnectionCommand, InitiateBankConnectionResponse>
{
    private const int AccessValidForDays = 90;
    private const int MaxHistoryCap = 730;

    public async Task<Result<InitiateBankConnectionResponse>> Handle(
        InitiateBankConnectionCommand command,
        CancellationToken cancellationToken)
    {
        if (!provider.IsConfigured)
        {
            return Result.Failure<InitiateBankConnectionResponse>(Error.Problem(
                "Bank.NotConfigured",
                "Conectarea băncii nu este disponibilă momentan."));
        }

        Guid userId = userContext.UserId;

        BankConnection? connection = await context.BankConnections
            .FirstOrDefaultAsync(bc => bc.UserId == userId, cancellationToken);

        if (connection is { Status: BankConnectionStatus.Linked })
        {
            return Result.Failure<InitiateBankConnectionResponse>(Error.Conflict(
                "Bank.AlreadyLinked",
                "Ai deja o bancă conectată. Deconecteaz-o înainte să conectezi alta."));
        }

        try
        {
            IReadOnlyList<BankInstitutionInfo> institutions =
                await provider.ListInstitutionsAsync("ro", cancellationToken);

            BankInstitutionInfo? institution = institutions
                .FirstOrDefault(i => i.Id == command.InstitutionId);

            if (institution is null)
            {
                return Result.Failure<InitiateBankConnectionResponse>(Error.NotFound(
                    "Bank.InstitutionNotFound",
                    "Banca selectată nu a fost găsită."));
            }

            // La reconectare, requisition-ul vechi este șters la provider (best-effort).
            if (connection is not null && !string.IsNullOrEmpty(connection.ProviderRequisitionId))
            {
                try
                {
                    await provider.DeleteRequisitionAsync(
                        secretProtector.Unprotect(connection.ProviderRequisitionId),
                        cancellationToken);
                }
                catch (BankDataProviderException)
                {
                    // Requisition-ul vechi poate fi deja expirat/șters — nu blocăm reconectarea.
                }
            }

            string reference = Guid.NewGuid().ToString("N");
            string baseUrl = configuration["App:BaseUrl"]
                ?? throw new InvalidOperationException("App:BaseUrl is missing in configuration.");
            // Fără query string: whitelist-ul de redirect-uri Enable Banking nu acceptă query.
            // Frontend-ul (DashboardPage) deschide tabul Banca pe baza parametrilor de callback.
            string redirect = $"{baseUrl.TrimEnd('/')}/app/dashboard";

            BankRequisitionCreated created = await provider.CreateRequisitionAsync(
                institution.Id,
                redirect,
                reference,
                maxHistoricalDays: Math.Min(Math.Max(institution.MaxHistoricalDays, 1), MaxHistoryCap),
                accessValidForDays: AccessValidForDays,
                cancellationToken);

            if (connection is null)
            {
                connection = new BankConnection
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                context.BankConnections.Add(connection);
            }

            connection.Provider = provider.ProviderName;
            connection.InstitutionId = institution.Id;
            connection.InstitutionName = institution.Name;
            connection.InstitutionLogoUrl = institution.Logo;
            connection.ProviderRequisitionId = secretProtector.Protect(created.RequisitionId);
            connection.ProviderAgreementId = created.AgreementId is null
                ? null
                : secretProtector.Protect(created.AgreementId);
            connection.Reference = reference;
            connection.Status = BankConnectionStatus.Created;
            connection.ConsentExpiresAtUtc = null;
            connection.ErrorMessage = null;
            connection.ConsecutiveFailures = 0;
            connection.ExpiryNotifiedAtUtc = null;
            connection.MaxHistoricalDays = Math.Min(Math.Max(institution.MaxHistoricalDays, 1), MaxHistoryCap);

            await context.SaveChangesAsync(cancellationToken);

            return new InitiateBankConnectionResponse(created.AuthorizationLink);
        }
        catch (BankDataProviderException ex)
        {
            return Result.Failure<InitiateBankConnectionResponse>(
                Error.Problem("Bank.ProviderError", ex.Message));
        }
    }
}
