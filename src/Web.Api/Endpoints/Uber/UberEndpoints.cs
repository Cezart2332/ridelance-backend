using Application.Abstractions.Messaging;
using Application.Uber;
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

        group.MapPost("imports", async (
            HttpRequest request,
            int? year,
            int? month,
            ICommandHandler<ImportUberCsvCommand, UberDashboardResponse> handler,
            CancellationToken cancellationToken) =>
        {
            IFormCollection form = await request.ReadFormAsync(cancellationToken);
            List<UberCsvUpload> uploads = [];
            foreach (IFormFile file in form.Files)
            {
                using var reader = new StreamReader(file.OpenReadStream());
                uploads.Add(new UberCsvUpload(file.FileName, await reader.ReadToEndAsync(cancellationToken)));
            }

            Result<UberDashboardResponse> result = await handler.Handle(new ImportUberCsvCommand(uploads, year, month), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery();

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
    }
}
