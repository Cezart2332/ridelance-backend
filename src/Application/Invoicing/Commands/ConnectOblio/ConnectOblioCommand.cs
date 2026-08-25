using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Security;
using Application.Abstractions.Services;
using Domain.Invoicing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Invoicing.Commands.ConnectOblio;

/// <summary>Leagă contul Oblio al proprietarului. Credențialele se verifică înainte de salvare.</summary>
public sealed record ConnectOblioCommand(
    string ClientId,
    string ClientSecret,
    string Cif,
    string? SeriesName) : ICommand<OblioConnectionDto>;

public sealed record OblioConnectionDto(
    bool Connected,
    string? CompanyName,
    string? Cif,
    string? SeriesName,
    IReadOnlyList<string> AvailableSeries,
    string? ErrorMessage,
    DateTime? LastSyncAtUtc);

internal sealed class ConnectOblioCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    ISecretProtector secrets,
    IOwnerInvoicingService invoicing)
    : ICommandHandler<ConnectOblioCommand, OblioConnectionDto>
{
    public async Task<Result<OblioConnectionDto>> Handle(
        ConnectOblioCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.ClientId) ||
            string.IsNullOrWhiteSpace(command.ClientSecret) ||
            string.IsNullOrWhiteSpace(command.Cif))
        {
            return Result.Failure<OblioConnectionDto>(Error.Problem(
                "Oblio.MissingCredentials",
                "Completează emailul contului, cheia API și CIF-ul."));
        }

        // Verificarea se face **înainte** de salvare: credențiale greșite salvate ca „conectat"
        // ar fi eșuat abia la prima factură, adică exact în momentul cel mai prost.
        OblioConnectionInfo info;
        try
        {
            info = await invoicing.TestConnectionAsync(
                new OwnerOblioCredentials(command.ClientId.Trim(), command.ClientSecret.Trim(), command.Cif.Trim()),
                cancellationToken);
        }
        catch (OblioApiException ex)
        {
            return Result.Failure<OblioConnectionDto>(Error.Problem("Oblio.ConnectionFailed", ex.Message));
        }

        OblioIntegration? integration = await context.OblioIntegrations
            .FirstOrDefaultAsync(o => o.UserId == userContext.UserId, cancellationToken);

        if (integration is null)
        {
            integration = new OblioIntegration { Id = Guid.NewGuid(), UserId = userContext.UserId };
            context.OblioIntegrations.Add(integration);
        }

        integration.ClientId = command.ClientId.Trim();
        integration.ClientSecretEncrypted = secrets.Protect(command.ClientSecret.Trim());
        integration.Cif = command.Cif.Trim();
        integration.CompanyName = info.CompanyName;
        integration.AvailableSeries = [.. info.InvoiceSeries];
        // Seria cerută, dacă e una dintre cele reale; altfel prima din cont.
        string? defaultSeries = info.InvoiceSeries.Count > 0 ? info.InvoiceSeries[0] : null;
        integration.SeriesName = info.InvoiceSeries.Contains(command.SeriesName ?? string.Empty)
            ? command.SeriesName
            : defaultSeries;
        integration.IsConnected = true;
        integration.ErrorMessage = null;
        integration.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new OblioConnectionDto(
            true,
            integration.CompanyName,
            integration.Cif,
            integration.SeriesName,
            info.InvoiceSeries,
            null,
            integration.LastSyncAtUtc));
    }
}
