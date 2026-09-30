using Application.Abstractions.Messaging;
using Application.Accounting.Assets;
using Application.Accounting.Contracts;
using Infrastructure.Authorization;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Accounting;

/// <summary>Activele PFA-ului și decizia de mijloc fix (spec registre §6): doar Admin / contabilitate.</summary>
internal sealed class AssetEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("accounting/pfas/{pfaId:guid}")
            .RequireAuthorization(Permissions.ManageAccounting)
            .WithTags(Tags.Accounting);

        group.MapGet("assets", async (Guid pfaId, DateOnly? asOf, IQueryHandler<ListAssetsQuery, IReadOnlyList<AssetDto>> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new ListAssetsQuery(pfaId, asOf), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapGet("assets/{id:guid}", async (Guid pfaId, Guid id, IQueryHandler<GetAssetQuery, AssetDetailDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new GetAssetQuery(pfaId, id), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPost("assets", async (Guid pfaId, ManualAssetRequest request, ICommandHandler<CreateManualAssetCommand, AssetDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new CreateManualAssetCommand(pfaId, request), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPut("assets/{id:guid}", async (Guid pfaId, Guid id, AssetClassificationRequest request, ICommandHandler<ClassifyAssetCommand, AssetDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new ClassifyAssetCommand(pfaId, id, request), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPost("assets/{id:guid}/dispose", async (Guid pfaId, Guid id, AssetDisposalRequest request, ICommandHandler<DisposeAssetCommand, AssetDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new DisposeAssetCommand(pfaId, id, request), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapGet("fixed-asset-candidates", async (Guid pfaId, IQueryHandler<ListFixedAssetCandidatesQuery, IReadOnlyList<FixedAssetCandidateDto>> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new ListFixedAssetCandidatesQuery(pfaId), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPost("ledger/{entryId:guid}/fixed-asset-decision", async (
            Guid pfaId,
            Guid entryId,
            FixedAssetDecisionRequest request,
            ICommandHandler<DecideFixedAssetCommand, AssetDto?> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new DecideFixedAssetCommand(pfaId, entryId, request.Decision, request.Name, request.Reason), cancellationToken))
                .Match(asset => asset is null ? Results.NoContent() : Results.Ok(asset), CustomResults.Problem));
    }
}
