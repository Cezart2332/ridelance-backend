using Application.Abstractions.Messaging;
using Application.Uber;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Uber;

internal sealed class UberEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("uber")
            .RequireAuthorization()
            .WithTags("Uber");

        // Clientul își vede datele Uber, dar nu le mai încarcă singur: rapoartele ajung
        // la birou, iar importul se face din Admin (vezi ruta de mai jos).
        group.MapGet("dashboard", async (
            string? period,
            int? year,
            int? month,
            IQueryHandler<GetUberDashboardQuery, UberDashboardResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<UberDashboardResponse> result = await handler.Handle(new GetUberDashboardQuery(period, year, month), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapGet("imports/{pfaRegistrationId:guid}", async (
            Guid pfaRegistrationId,
            string? period,
            int? year,
            int? month,
            IQueryHandler<GetUberDashboardForPfaQuery, UberDashboardResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<UberDashboardResponse> result = await handler.Handle(
                new GetUberDashboardForPfaQuery(pfaRegistrationId, period, year, month),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .HasPermission(Permissions.ManageClientIncome);

        group.MapPost("imports/{pfaRegistrationId:guid}", async (
            Guid pfaRegistrationId,
            HttpRequest request,
            int? year,
            int? month,
            ICommandHandler<ImportUberCsvForPfaCommand, UberDashboardResponse> handler,
            CancellationToken cancellationToken) =>
        {
            IFormCollection form = await request.ReadFormAsync(cancellationToken);
            List<UberCsvUpload> uploads = [];
            foreach (IFormFile file in form.Files)
            {
                using var reader = new StreamReader(file.OpenReadStream());
                uploads.Add(new UberCsvUpload(file.FileName, await reader.ReadToEndAsync(cancellationToken)));
            }

            Result<UberDashboardResponse> result = await handler.Handle(
                new ImportUberCsvForPfaCommand(pfaRegistrationId, uploads, year, month),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .HasPermission(Permissions.ManageClientIncome)
        .DisableAntiforgery();
    }
}
