using Application.Abstractions.Messaging;
using Application.Admin.Oblio;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Admin;

internal sealed class AdminOblioEndpoints : IEndpoint
{
    public sealed record TestInvoiceRequest(string? ClientName, decimal AmountLei, string? Description);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("admin/oblio/status", async (
            IQueryHandler<GetOblioStatusQuery, OblioStatusResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<OblioStatusResponse> result = await handler.Handle(new GetOblioStatusQuery(), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ViewServiceOrders)
        .WithTags(Tags.Admin);

        app.MapPost("admin/oblio/test-invoice", async (
            TestInvoiceRequest request,
            ICommandHandler<CreateOblioTestInvoiceCommand, OblioTestInvoiceResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateOblioTestInvoiceCommand(
                request.ClientName,
                request.AmountLei,
                request.Description);

            Result<OblioTestInvoiceResponse> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ViewServiceOrders)
        .WithTags(Tags.Admin);

        app.MapGet("admin/oblio/invoices", async (
            int? limit,
            IQueryHandler<GetIssuedInvoicesQuery, List<IssuedInvoiceDto>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<List<IssuedInvoiceDto>> result = await handler.Handle(
                new GetIssuedInvoicesQuery(limit ?? 50),
                cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ViewServiceOrders)
        .WithTags(Tags.Admin);
    }
}
