using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Invoicing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Invoicing.Queries.GetOwnerInvoices;

public sealed record GetOblioCredentialsQuery : IQuery<OblioCredentialsStatus>;
public sealed record OblioCredentialsStatus(bool Connected, string? AccountEmail, bool HasApiKey,
    string? CompanyName, string? Cif, string? SeriesName, IReadOnlyList<string> AvailableSeries,
    string? ErrorMessage, DateTime? LastSyncAtUtc);

internal sealed class GetOblioCredentialsQueryHandler(IApplicationDbContext context, IUserContext userContext)
    : IQueryHandler<GetOblioCredentialsQuery, OblioCredentialsStatus>
{
    public async Task<Result<OblioCredentialsStatus>> Handle(GetOblioCredentialsQuery query, CancellationToken cancellationToken)
    {
        OblioIntegration? integration = await context.OblioIntegrations.AsNoTracking()
            .SingleOrDefaultAsync(o => o.UserId == userContext.UserId, cancellationToken);
        return new OblioCredentialsStatus(integration?.IsConnected == true, integration?.ClientId,
            !string.IsNullOrEmpty(integration?.ClientSecretEncrypted), integration?.CompanyName,
            integration?.Cif, integration?.SeriesName, integration?.AvailableSeries ?? [],
            integration?.ErrorMessage, integration?.LastSyncAtUtc);
    }
}
