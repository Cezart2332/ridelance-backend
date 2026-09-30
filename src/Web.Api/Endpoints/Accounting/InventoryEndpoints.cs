using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Inventory;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Accounting;

/// <summary>
/// Inventarierea (spec registre §5): Adminul o pornește, revizuiește și finalizează
/// (<c>accounting/pfas/{pfaId}/inventory-counts</c>); PFA-ul își confirmă inventarul (<c>pfa/inventory</c>).
/// </summary>
internal sealed class InventoryEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder admin = app.MapGroup("accounting/pfas/{pfaId:guid}/inventory-counts")
            .RequireAuthorization(Permissions.ManageAccounting)
            .WithTags(Tags.Accounting);

        admin.MapGet(string.Empty, async (Guid pfaId, IQueryHandler<ListInventoryCountsQuery, IReadOnlyList<InventoryCountDto>> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new ListInventoryCountsQuery(pfaId), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        admin.MapPost(string.Empty, async (Guid pfaId, StartInventoryCountRequest request, ICommandHandler<StartInventoryCountCommand, InventoryCountDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new StartInventoryCountCommand(pfaId, request.Date, request.Reason), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        admin.MapPatch("{id:guid}/items/{itemId:guid}", async (
            Guid pfaId, Guid id, Guid itemId, InventoryItemRequest request, ICommandHandler<UpdateInventoryItemCommand, InventoryCountDto> handler, CancellationToken cancellationToken) =>
            TryAction(request.Action, out InventoryItemAction action)
                ? (await handler.Handle(new UpdateInventoryItemCommand(pfaId, id, itemId, action, request.Value, request.Note, ByAdmin: true), cancellationToken)).Match(Results.Ok, CustomResults.Problem)
                : InvalidAction());

        admin.MapPost("{id:guid}/items", async (
            Guid pfaId, Guid id, AddInventoryItemRequest request, ICommandHandler<AddInventoryItemCommand, InventoryCountDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new AddInventoryItemCommand(pfaId, id, request.Category, request.Description, request.Value, request.Note, ByAdmin: true), cancellationToken))
                .Match(Results.Ok, CustomResults.Problem));

        admin.MapPost("{id:guid}/finalize", async (Guid pfaId, Guid id, ICommandHandler<FinalizeInventoryCountCommand, InventoryCountDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new FinalizeInventoryCountCommand(pfaId, id), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        RouteGroupBuilder client = app.MapGroup("pfa/inventory")
            .RequireAuthorization()
            .WithTags(Tags.Accounting);

        client.MapGet(string.Empty, async (IQueryHandler<GetClientInventoryQuery, InventoryCountDto?> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new GetClientInventoryQuery(), cancellationToken)).Match(count => count is null ? Results.NoContent() : Results.Ok(count), CustomResults.Problem));

        client.MapPatch("{id:guid}/items/{itemId:guid}", async (
            Guid id, Guid itemId, InventoryItemRequest request, ICommandHandler<ClientInventoryItemCommand, InventoryCountDto> handler, CancellationToken cancellationToken) =>
            TryAction(request.Action, out InventoryItemAction action) && action != InventoryItemAction.Note
                ? (await handler.Handle(new ClientInventoryItemCommand(id, itemId, action, request.Value, request.Note), cancellationToken)).Match(Results.Ok, CustomResults.Problem)
                : InvalidAction());

        client.MapPost("{id:guid}/items", async (Guid id, AddInventoryItemRequest request, ICommandHandler<ClientAddInventoryItemCommand, InventoryCountDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new ClientAddInventoryItemCommand(id, request.Category, request.Description, request.Value, request.Note), cancellationToken))
                .Match(Results.Ok, CustomResults.Problem));

        client.MapPost("{id:guid}/submit", async (Guid id, ICommandHandler<ClientSubmitInventoryCommand, InventoryCountDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new ClientSubmitInventoryCommand(id), cancellationToken)).Match(Results.Ok, CustomResults.Problem));
    }

    private static bool TryAction(string? value, out InventoryItemAction action) =>
        Enum.TryParse(value, ignoreCase: true, out action) && Enum.IsDefined(action);

    private static IResult InvalidAction() =>
        Results.Problem(title: "Accounting.InventoryInvalid", detail: "Acțiunea e CONFIRM, ADJUST, REMOVE sau NOTE.", statusCode: StatusCodes.Status400BadRequest);
}
