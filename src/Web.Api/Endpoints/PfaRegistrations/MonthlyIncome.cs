using Application.Abstractions.Messaging;
using Application.PfaRegistrations.MonthlyIncome;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

internal sealed class MonthlyIncome : IEndpoint
{
    public sealed record UpsertRequest(
        int Year,
        int Month,
        decimal VenitCash,
        decimal VenitCard,
        decimal VenitBolt,
        decimal VenitUber);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("pfa-registrations/{id:guid}/monthly-income", async (
            Guid id,
            int year,
            int month,
            IQueryHandler<GetPfaMonthlyIncomeQuery, PfaMonthlyIncomeResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetPfaMonthlyIncomeQuery(id, year, month);
            Result<PfaMonthlyIncomeResponse> result = await handler.Handle(query, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        app.MapPut("pfa-registrations/{id:guid}/monthly-income", async (
            Guid id,
            UpsertRequest request,
            ICommandHandler<UpsertPfaMonthlyIncomeCommand, PfaMonthlyIncomeResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new UpsertPfaMonthlyIncomeCommand(
                id,
                request.Year,
                request.Month,
                request.VenitCash,
                request.VenitCard,
                request.VenitBolt,
                request.VenitUber);

            Result<PfaMonthlyIncomeResponse> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageClientIncome)
        .WithTags(Tags.PfaRegistrations);

        app.MapPut("pfa-registrations/{id:guid}/monthly-income/process", async (
            Guid id,
            int year,
            int month,
            bool isProcessed,
            ICommandHandler<ProcessPfaMonthlyIncomeCommand, PfaMonthlyIncomeResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new ProcessPfaMonthlyIncomeCommand(id, year, month, isProcessed);
            Result<PfaMonthlyIncomeResponse> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageClientIncome)
        .WithTags(Tags.PfaRegistrations);
    }
}
