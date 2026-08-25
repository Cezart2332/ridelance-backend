using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.Invoicing.Commands.CancelInvoice;
using Application.Invoicing.Commands.CollectInvoice;
using Application.Invoicing.Commands.ConnectOblio;
using Application.Invoicing.Commands.DisconnectOblio;
using Application.Invoicing.Commands.IssueInvoice;
using Application.Invoicing.Queries.GetOwnerInvoices;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Companies;

/// <summary>
/// Facturile emise ale proprietarului, pe contul lui Oblio.
///
/// Nicio rută nu poartă id de proprietar: handlerele citesc contul din `IUserContext`, deci
/// nimeni nu poate ajunge la facturile altcuiva printr-un URL modificat.
/// </summary>
internal sealed class GetInvoices : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("invoices", async (
            DateOnly? from,
            DateOnly? to,
            IQueryHandler<GetOwnerInvoicesQuery, OwnerInvoicesDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<OwnerInvoicesDto> result = await handler.Handle(
                new GetOwnerInvoicesQuery(from, to),
                cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}

internal sealed class ConnectOblio : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("invoices/oblio/connect", async (
            ConnectOblioCommand command,
            ICommandHandler<ConnectOblioCommand, OblioConnectionDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<OblioConnectionDto> result = await handler.Handle(command, cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}

internal sealed class DisconnectOblio : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("invoices/oblio", async (
            ICommandHandler<DisconnectOblioCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new DisconnectOblioCommand(), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}

internal sealed class CollectInvoice : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("invoices/collect", async (
            CollectInvoiceCommand command,
            ICommandHandler<CollectInvoiceCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(command, cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}

internal sealed class CancelInvoice : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("invoices/cancel", async (
            CancelInvoiceCommand command,
            ICommandHandler<CancelInvoiceCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(command, cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}

internal sealed class IssueInvoice : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("invoices/issue", async (
            IssueInvoiceCommand command,
            ICommandHandler<IssueInvoiceCommand, IssuedInvoiceResult> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IssuedInvoiceResult> result = await handler.Handle(command, cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}

/// <summary>
/// Datele publice ale unei firme, pentru precompletarea facturii.
/// </summary>
/// <remarks>
/// Registrul e public, dar endpointul cere autentificare: fără ea, ar fi fost un proxy deschis
/// peste ANAF, pe socoteala noastră.
/// </remarks>
internal sealed class LookupCompany : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("invoices/company/{cui}", async (
            string cui,
            ICompanyLookupService lookup,
            CancellationToken cancellationToken) =>
        {
            CompanyLookupResult? company = await lookup.FindByCuiAsync(cui, cancellationToken);
            return company is null ? Results.NotFound() : Results.Ok(company);
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}
