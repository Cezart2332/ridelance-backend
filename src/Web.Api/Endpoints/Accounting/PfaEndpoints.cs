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
