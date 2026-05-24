using Application.Abstractions.Messaging;
using Application.Cars.Commands.RecordCarAnalytics;
using Domain.Cars;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Cars;

internal sealed class RecordAnalytics : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("cars/{id:guid}/analytics/view", (Guid id, ICommandHandler<RecordCarAnalyticsCommand> handler, CancellationToken ct) =>
            Record(id, CarAnalyticsEventType.View, handler, ct))
            .AllowAnonymous()
            .WithTags(Tags.Cars);

        app.MapPost("cars/{id:guid}/analytics/click", (Guid id, ICommandHandler<RecordCarAnalyticsCommand> handler, CancellationToken ct) =>
            Record(id, CarAnalyticsEventType.Click, handler, ct))
            .AllowAnonymous()
            .WithTags(Tags.Cars);
    }

    private static async Task<IResult> Record(
        Guid carId,
        CarAnalyticsEventType eventType,
        ICommandHandler<RecordCarAnalyticsCommand> handler,
        CancellationToken cancellationToken)
    {
        Result result = await handler.Handle(new RecordCarAnalyticsCommand(carId, eventType), cancellationToken);
        return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
    }
}
