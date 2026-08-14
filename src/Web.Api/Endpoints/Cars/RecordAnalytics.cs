using Application.Abstractions.Messaging;
using Application.Cars;
using Application.Cars.Commands.RecordCarClick;
using Application.Cars.Commands.RecordCarView;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Cars;

internal sealed class RecordAnalytics : IEndpoint
{
    /// <summary>Folosit dacă nu e configurat `Cars:ViewSalt`. Schimbă hash-urile, nu le slăbește.</summary>
    private const string FallbackSalt = "ridelance-car-views";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Ruta rămâne cea veche: serviciul din frontend și dashboardurile o folosesc deja.
        app.MapPost("cars/{id:guid}/analytics/view", async (
            Guid id,
            [FromBody] RecordViewRequest? request,
            HttpContext httpContext,
            IConfiguration configuration,
            ICommandHandler<RecordCarViewCommand> handler,
            CancellationToken cancellationToken) =>
        {
            string visitorHash = VisitorFingerprint.Compute(
                httpContext.Connection.RemoteIpAddress?.ToString(),
                httpContext.Request.Headers.UserAgent.ToString(),
                configuration["Cars:ViewSalt"] ?? FallbackSalt);

            var command = new RecordCarViewCommand(id, visitorHash, request?.Source ?? "vdp");
            Result result = await handler.Handle(command, cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .AllowAnonymous()
        .WithTags(Tags.Cars);

        app.MapPost("cars/{id:guid}/analytics/click", async (
            Guid id,
            ICommandHandler<RecordCarClickCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new RecordCarClickCommand(id), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .AllowAnonymous()
        .WithTags(Tags.Cars);
    }
}

internal sealed record RecordViewRequest(string? Source);
