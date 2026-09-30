using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Handover;
using Application.Accounting.Pfas;
using Application.Accounting.Periods;
using Application.Accounting.Registers;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Accounting;

/// <summary>Dosarul PFA: sumar, inactivare, dosar de predare, perioade contabile (spec contabilitate §4.1, §4.6, B8).</summary>
internal sealed class PfaEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("accounting")
            .RequireAuthorization(Permissions.ManageAccounting)
            .WithTags(Tags.Accounting);

        group.MapGet("pfas/{pfaId:guid}/summary", async (Guid pfaId, IQueryHandler<GetPfaSummaryQuery, PfaAccountingSummary> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new GetPfaSummaryQuery(pfaId), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPost("pfas/{pfaId:guid}/deactivate", async (
            Guid pfaId,
            DeactivateRequest request,
            ICommandHandler<DeactivatePfaCommand, PfaAccountingSummary> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new DeactivatePfaCommand(pfaId, request.AccountingEndDate), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPost("pfas/{pfaId:guid}/handover-package", async (Guid pfaId, ICommandHandler<StartHandoverPackageCommand, JobRef> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new StartHandoverPackageCommand(pfaId), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapGet("jobs/{jobId:guid}/file", async (Guid jobId, IQueryHandler<GetJobFileQuery, RegisterFile> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new GetJobFileQuery(jobId), cancellationToken))
                .Match(file => Results.File(file.Content, file.ContentType, file.FileName), CustomResults.Problem));

        group.MapGet("pfas/{pfaId:guid}/periods", async (Guid pfaId, IQueryHandler<ListPeriodsQuery, IReadOnlyList<AccountingPeriodDto>> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new ListPeriodsQuery(pfaId), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPost("pfas/{pfaId:guid}/periods/{period}/close", async (
            Guid pfaId,
            string period,
            ICommandHandler<ClosePeriodCommand, AccountingPeriodDto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new ClosePeriodCommand(pfaId, period), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        // Spec flux contabil §8: checklist-ul lunii, pe care îl verifică și închiderea.
        group.MapGet("pfas/{pfaId:guid}/periods/{period}/reconciliation", async (
            Guid pfaId,
            string period,
            IQueryHandler<GetMonthReconciliationQuery, MonthReconciliationDto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new GetMonthReconciliationQuery(pfaId, period), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        // Registre §7: explicația Adminului pentru Z vs cash platformă și payout-urile nereconciliate.
        group.MapPost("pfas/{pfaId:guid}/periods/{period}/explanations", async (
            Guid pfaId,
            string period,
            ExplainControlRequest request,
            ICommandHandler<ExplainReconciliationControlCommand> handler,
            CancellationToken cancellationToken) =>
            Enum.TryParse(request.Control?.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true, out ReconciliationControl control)
                ? (await handler.Handle(new ExplainReconciliationControlCommand(pfaId, period, control, request.Note), cancellationToken)).Match(Results.NoContent, CustomResults.Problem)
                : Results.Problem(title: "Accounting.ControlNotExplainable", detail: "Controlul nu există.", statusCode: StatusCodes.Status400BadRequest));

        // Registre §7: anul contabil — starea, „Închide anul”, redeschiderea (ADMIN) și pachetul anual.
        group.MapGet("pfas/{pfaId:guid}/years/{year:int}", async (Guid pfaId, int year, IQueryHandler<GetAccountingYearQuery, AccountingYearDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new GetAccountingYearQuery(pfaId, year), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPost("pfas/{pfaId:guid}/years/{year:int}/close", async (Guid pfaId, int year, ICommandHandler<CloseAccountingYearCommand, AccountingYearDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new CloseAccountingYearCommand(pfaId, year), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPost("pfas/{pfaId:guid}/years/{year:int}/reopen", async (
            Guid pfaId, int year, ReopenPeriodRequest request, ICommandHandler<ReopenAccountingYearCommand, AccountingYearDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new ReopenAccountingYearCommand(pfaId, year, request.Reason), cancellationToken)).Match(Results.Ok, CustomResults.Problem))
            .RequireAuthorization(Permissions.ManagePfaRegistrations);

        group.MapGet("pfas/{pfaId:guid}/years/{year:int}/package", async (Guid pfaId, int year, IQueryHandler<GetYearPackageQuery, Application.Accounting.Registers.RegisterFile> handler, CancellationToken cancellationToken) =>
            RegisterEndpoints.File(await handler.Handle(new GetYearPackageQuery(pfaId, year), cancellationToken)));

        // Redeschiderea: doar ADMIN, cu motiv.
        group.MapPost("pfas/{pfaId:guid}/periods/{period}/reopen", async (
            Guid pfaId,
            string period,
            ReopenPeriodRequest request,
            ICommandHandler<ReopenPeriodCommand, AccountingPeriodDto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new ReopenPeriodCommand(pfaId, period, request.Reason), cancellationToken)).Match(Results.Ok, CustomResults.Problem))
            .RequireAuthorization(Permissions.ManagePfaRegistrations);

        group.MapPost("pfas/{pfaId:guid}/periods/{period}/corrections", async (
            Guid pfaId,
            string period,
            PeriodCorrectionRequest request,
            ICommandHandler<CreatePeriodCorrectionCommand, PeriodCorrectionDto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new CreatePeriodCorrectionCommand(pfaId, period, request.LedgerEntryId, request.Change, request.Reason), cancellationToken))
                .Match(Results.Ok, CustomResults.Problem));
    }
}

internal sealed record ReopenPeriodRequest(string? Reason);

internal sealed record ExplainControlRequest(string? Control, string? Note);

