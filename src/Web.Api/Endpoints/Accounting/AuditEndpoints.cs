using Application.Abstractions.Messaging;
using Application.Accounting.Audit;
using Application.Accounting.Contracts;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Accounting;

/// <summary>Jurnalul de audit al unui PFA (spec contabilitate §4.1, B5).</summary>
internal sealed class AuditEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("accounting/pfas/{pfaId:guid}/audit", async (
            Guid pfaId,
            DateOnly? from,
            DateOnly? to,
            string? entity,
            IQueryHandler<ListPfaAuditQuery, IReadOnlyList<AuditEntryDto>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<AuditEntryDto>> result = await handler.Handle(new ListPfaAuditQuery(pfaId, from, to, entity), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization(Permissions.ManageAccounting)
        .WithTags(Tags.Accounting);
    }
}
