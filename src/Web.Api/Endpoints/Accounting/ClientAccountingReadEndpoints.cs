using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Anaf;
using Application.Accounting.Contracts;
using Application.Accounting.Declarations;
using Application.Accounting.FiscalRegister;
using Application.Accounting.Ledger;
using Application.Accounting.Pfas;
using Application.Accounting.Registers;
using Application.Accounting.Spv;
using Application.Payments;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Accounting;

/// <summary>Owner-only views. No supplied PFA id and no staff mutation endpoints.</summary>
internal sealed class ClientAccountingReadEndpoints : IEndpoint
{
    public sealed record OwnMonthlyRequest(string Period);
    public sealed record OwnAnnualRequest(int Year);

    private const string OwnerKey = "ClientAccounting.Owner";
    private static Guid Owner(HttpContext context) => (Guid)context.Items[OwnerKey]!;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("pfa/accounting").RequireAuthorization().WithTags(Tags.Accounting);
        group.AddEndpointFilter(async (context, next) =>
        {
            IServiceProvider services = context.HttpContext.RequestServices;
            Guid userId = services.GetRequiredService<IUserContext>().UserId;
            Guid? pfaId = await ClientAccountingScope.PfaIdAsync(services.GetRequiredService<IApplicationDbContext>(), userId, context.HttpContext.RequestAborted);
            if (pfaId is null)
            {
                return Results.NotFound();
            }
            context.HttpContext.Items[OwnerKey] = pfaId.Value;
            return await next(context);
        });

        // Registrele sunt ale PFAlone: și le ține singur. La PFA Full le ține contabilul, iar clientul
        // nu le mai vede.
        RouteGroupBuilder registers = group.MapGroup("registers");
        registers.AddEndpointFilter(async (context, next) =>
        {
            IServiceProvider services = context.HttpContext.RequestServices;
            Guid userId = services.GetRequiredService<IUserContext>().UserId;
            return await PlanAccess.ManagesOwnBooksAsync(services.GetRequiredService<IApplicationDbContext>(), userId, context.HttpContext.RequestAborted)
                ? await next(context)
                : Results.Forbid();
        });

        registers.MapGet("rjip", async (HttpContext context, DateOnly from, DateOnly to, IQueryHandler<GetRjipQuery, RjipView> handler, CancellationToken ct) =>
            (await handler.Handle(new GetRjipQuery(Owner(context), from, to), ct)).Match(Results.Ok, CustomResults.Problem));
        registers.MapGet("ref", async (HttpContext context, int year, IQueryHandler<GetRefQuery, RefView> handler, CancellationToken ct) =>
            (await handler.Handle(new GetRefQuery(Owner(context), year), ct)).Match(Results.Ok, CustomResults.Problem));
        registers.MapGet("rjip/export", async (HttpContext context, DateOnly from, DateOnly to, IQueryHandler<ExportRjipQuery, RegisterFile> handler, CancellationToken ct) =>
            RegisterEndpoints.File(await handler.Handle(new ExportRjipQuery(Owner(context), from, to, RegisterFormat.Pdf), ct)));
        registers.MapGet("ref/export", async (HttpContext context, int year, IQueryHandler<ExportRefQuery, RegisterFile> handler, CancellationToken ct) =>
            RegisterEndpoints.File(await handler.Handle(new ExportRefQuery(Owner(context), year, RegisterFormat.Pdf, null), ct)));
        registers.MapGet("inventory/export", async (HttpContext context, int year, IQueryHandler<Application.Accounting.Inventory.ExportInventoryQuery, RegisterFile> handler, CancellationToken ct) =>
            RegisterEndpoints.File(await handler.Handle(new Application.Accounting.Inventory.ExportInventoryQuery(Owner(context), year, RegisterFormat.Pdf), ct)));

        // PFAlone își trece singur încasările și plățile; nimic nu se completează automat.
        registers.MapPost("entries", async (HttpContext context, ManualLedgerEntryRequest request, ICommandHandler<AddOwnLedgerEntryCommand, LedgerEntryDto> handler, CancellationToken ct) =>
            (await handler.Handle(new AddOwnLedgerEntryCommand(Owner(context), request), ct)).Match(Results.Ok, CustomResults.Problem));
        registers.MapDelete("entries/{id:guid}", async (HttpContext context, Guid id, ICommandHandler<DeleteOwnLedgerEntryCommand> handler, CancellationToken ct) =>
            (await handler.Handle(new DeleteOwnLedgerEntryCommand(Owner(context), id), ct)).Match(Results.NoContent, CustomResults.Problem));

        // Generatorul de declarații al PFAlone: XML-urile le depune el. Planul îl verifică handlerul.
        RouteGroupBuilder own = group.MapGroup("own-declarations");
        own.MapGet(string.Empty, async (int year, IQueryHandler<GetOwnDeclarationsQuery, IReadOnlyList<OwnDeclarationDto>> handler, CancellationToken ct) =>
            (await handler.Handle(new GetOwnDeclarationsQuery(year), ct)).Match(Results.Ok, CustomResults.Problem));
        own.MapPost("monthly", async (OwnMonthlyRequest request, ICommandHandler<GenerateOwnMonthlyDeclarationsCommand, OwnGenerationResult> handler, CancellationToken ct) =>
            (await handler.Handle(new GenerateOwnMonthlyDeclarationsCommand(request.Period), ct)).Match(Results.Ok, CustomResults.Problem));
        own.MapPost("annual", async (OwnAnnualRequest request, ICommandHandler<GenerateOwnAnnualDeclarationsCommand, OwnGenerationResult> handler, CancellationToken ct) =>
            (await handler.Handle(new GenerateOwnAnnualDeclarationsCommand(request.Year), ct)).Match(Results.Ok, CustomResults.Problem));

        group.MapGet("spv", async (HttpContext context, IQueryHandler<GetPfaSpvQuery, PfaSpvDto> handler, CancellationToken ct) =>
            (await handler.Handle(new GetPfaSpvQuery(Owner(context), LinkedOnly: true), ct)).Match(Results.Ok, CustomResults.Problem));
        group.MapGet("efactura", async (HttpContext context, IQueryHandler<GetPfaEFacturaQuery, PfaEFacturaDto> handler, CancellationToken ct) =>
            (await handler.Handle(new GetPfaEFacturaQuery(Owner(context)), ct)).Match(dto => Results.Ok(new { dto.Link, dto.Messages }), CustomResults.Problem));
        group.MapGet("spv/{id:guid}/file", async (HttpContext context, Guid id, IApplicationDbContext db, IQueryHandler<GetSpvMessageFileQuery, SpvFile> handler, CancellationToken ct) =>
            await ClientAccountingScope.OwnsSpvMessageAsync(db, Owner(context), id, ct)
                ? (await handler.Handle(new GetSpvMessageFileQuery(id), ct)).Match(file => Results.File(file.Content, file.ContentType, file.FileName), CustomResults.Problem)
                : Results.NotFound());
        group.MapGet("efactura/{id:guid}/{kind}", async (HttpContext context, Guid id, string kind, IApplicationDbContext db, IQueryHandler<GetEFacturaFileQuery, AnafFile> handler, CancellationToken ct) =>
        {
            if (kind is not ("xml" or "pdf"))
            {
                return Results.BadRequest();
            }
            if (!await ClientAccountingScope.OwnsInvoiceAsync(db, Owner(context), id, ct))
            {
                return Results.NotFound();
            }
            return (await handler.Handle(new GetEFacturaFileQuery(id, kind == "pdf" ? EFacturaFileKind.Pdf : EFacturaFileKind.Xml), ct))
                .Match(file => Results.File(file.Content, file.ContentType, file.FileName), CustomResults.Problem);
        });
    }
}
