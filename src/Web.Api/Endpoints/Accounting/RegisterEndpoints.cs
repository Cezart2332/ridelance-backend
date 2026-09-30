using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.FiscalRegister;
using Application.Accounting.Inventory;
using Application.Accounting.Registers;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Accounting;

/// <summary>Registrele contabile (RJIP, REF, Registru-inventar) și activele (spec contabilitate §4.6, B7).</summary>
internal sealed class RegisterEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("accounting/pfas/{pfaId:guid}")
            .RequireAuthorization(Permissions.ManageAccounting)
            .WithTags(Tags.Accounting);

        group.MapGet("registers/rjip", async (Guid pfaId, DateOnly from, DateOnly to, bool? regenerate, IQueryHandler<GetRjipQuery, RjipView> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new GetRjipQuery(pfaId, from, to, regenerate ?? false), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapGet("registers/rjip/export", async (
            Guid pfaId,
            DateOnly from,
            DateOnly to,
            string? format,
            IQueryHandler<ExportRjipQuery, RegisterFile> handler,
            CancellationToken cancellationToken) =>
            TryFormat(format, out RegisterFormat parsed)
                ? File(await handler.Handle(new ExportRjipQuery(pfaId, from, to, parsed), cancellationToken))
                : InvalidFormat());

        group.MapGet("registers/ref", async (Guid pfaId, int year, IQueryHandler<GetRefQuery, RefView> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new GetRefQuery(pfaId, year), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapGet("registers/ref/export", async (
            Guid pfaId,
            int year,
            string? format,
            DateOnly? asOf,
            IQueryHandler<ExportRefQuery, RegisterFile> handler,
            CancellationToken cancellationToken) =>
            TryFormat(format, out RegisterFormat parsed)
                ? File(await handler.Handle(new ExportRefQuery(pfaId, year, parsed, asOf), cancellationToken))
                : InvalidFormat());

        group.MapGet("registers/inventory/export", async (
            Guid pfaId,
            int year,
            string? format,
            IQueryHandler<ExportInventoryQuery, RegisterFile> handler,
            CancellationToken cancellationToken) =>
            TryFormat(format, out RegisterFormat parsed)
                ? File(await handler.Handle(new ExportInventoryQuery(pfaId, year, parsed), cancellationToken))
                : InvalidFormat());
    }

    /// <summary><c>pdf</c> (implicit), <c>xlsx</c> sau <c>csv</c>, ca în contract.</summary>
    private static bool TryFormat(string? format, out RegisterFormat parsed)
    {
        parsed = RegisterFormat.Pdf;
        switch (format?.ToUpperInvariant())
        {
            case null or "" or "PDF":
                return true;
            case "XLSX":
                parsed = RegisterFormat.Xlsx;
                return true;
            case "CSV":
                parsed = RegisterFormat.Csv;
                return true;
            default:
                return false;
        }
    }

    private static IResult InvalidFormat() =>
        Results.Problem(title: "Accounting.InvalidFormat", detail: "Formatul exportului e „pdf” sau „xlsx”.", statusCode: StatusCodes.Status400BadRequest);

    private static IResult File(Result<RegisterFile> result) =>
        result.Match(file => Results.File(file.Content, file.ContentType, file.FileName), CustomResults.Problem);
}
