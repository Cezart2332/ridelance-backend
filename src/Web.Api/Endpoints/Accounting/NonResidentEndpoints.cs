using Application.Abstractions.Messaging;
using Application.Accounting.NonResident;
using Domain.Accounting;
using Infrastructure.Authorization;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Accounting;

/// <summary>Deciziile de impozit nerezident (spec declarații F20–F25): coada de confirmare a Adminului.</summary>
internal sealed class NonResidentEndpoints : IEndpoint
{
    public sealed record ConfirmRequest(string? Reason);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("accounting")
            .RequireAuthorization(Permissions.ManageAccounting)
            .WithTags(Tags.Accounting);

        group.MapGet("pfas/{pfaId:guid}/non-resident-decisions", async (
            Guid pfaId,
            string? status,
            IQueryHandler<ListNonResidentDecisionsQuery, IReadOnlyList<NonResidentDecisionDto>> handler,
            CancellationToken cancellationToken) =>
        {
            NonResidentDecisionStatus? filter = Enum.TryParse(status?.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true, out NonResidentDecisionStatus parsed)
                ? parsed
                : null;
            return (await handler.Handle(new ListNonResidentDecisionsQuery(pfaId, filter), cancellationToken)).Match(Results.Ok, CustomResults.Problem);
        });

        // Confirmarea unei reguli juridice: doar ADMIN.
        group.MapPost("non-resident-decisions/{id:guid}/confirm", async (
            Guid id,
            ConfirmRequest request,
            ICommandHandler<ConfirmNonResidentDecisionCommand, NonResidentDecisionDto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new ConfirmNonResidentDecisionCommand(id, request.Reason), cancellationToken)).Match(Results.Ok, CustomResults.Problem))
            .RequireAuthorization(Permissions.ManagePfaRegistrations);
    }
}
