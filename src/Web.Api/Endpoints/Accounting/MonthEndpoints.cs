using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Months;
using Domain.Accounting;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Accounting;

/// <summary>Luna fiscală pe toate PFA-urile, joburile și declarațiile unui PFA (spec contabilitate §4.3, §4.4, B3–B4).</summary>
internal sealed class MonthEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("accounting")
            .RequireAuthorization(Permissions.ManageAccounting)
            .WithTags(Tags.Accounting);

        group.MapGet("periods/{period}/overview", async (
            string period,
            IQueryHandler<GetPeriodOverviewQuery, PeriodOverview> handler,
            CancellationToken cancellationToken) =>
        {
            Result<PeriodOverview> result = await handler.Handle(new GetPeriodOverviewQuery(period), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost("periods/{period}/process", (string period, ICommandHandler<StartMonthJobCommand, JobRef> handler, CancellationToken cancellationToken) =>
            Start(BackgroundJobType.ProcessPeriod, period, handler, cancellationToken));

        group.MapPost("periods/{period}/generate", (string period, ICommandHandler<StartMonthJobCommand, JobRef> handler, CancellationToken cancellationToken) =>
            Start(BackgroundJobType.GenerateDeclarations, period, handler, cancellationToken));

        group.MapPost("periods/{period}/validate", (string period, ICommandHandler<StartMonthJobCommand, JobRef> handler, CancellationToken cancellationToken) =>
            Start(BackgroundJobType.ValidateDeclarations, period, handler, cancellationToken));

        group.MapPost("periods/{period}/confirm-clean-documents", async (
            string period,
            ICommandHandler<ConfirmCleanDocumentsCommand, ConfirmBulkResult> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ConfirmBulkResult> result = await handler.Handle(new ConfirmCleanDocumentsCommand(period), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapGet("jobs/{jobId:guid}", async (
            Guid jobId,
            IQueryHandler<GetJobQuery, JobDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<JobDto> result = await handler.Handle(new GetJobQuery(jobId), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapGet("pfas/{pfaId:guid}/declarations", async (
            Guid pfaId,
            string period,
            IQueryHandler<ListDeclarationsQuery, IReadOnlyList<DeclarationSummary>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<DeclarationSummary>> result = await handler.Handle(new ListDeclarationsQuery(pfaId, period), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });
    }

    private static async Task<IResult> Start(
        BackgroundJobType type,
        string period,
        ICommandHandler<StartMonthJobCommand, JobRef> handler,
        CancellationToken cancellationToken)
    {
        Result<JobRef> result = await handler.Handle(new StartMonthJobCommand(type, period), cancellationToken);
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}
