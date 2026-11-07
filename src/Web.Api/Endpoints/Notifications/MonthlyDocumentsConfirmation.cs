using Application.Abstractions.Messaging;
using Application.Notifications.RecurringDocumentation;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Notifications;

/// <summary>„Nu am bonuri de încărcat” pentru o lună și lista lunilor confirmate.</summary>
internal sealed class MonthlyDocumentsConfirmation : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("notifications/monthly-documents/{year:int}/{month:int}/confirm", async (
            int year,
            int month,
            ICommandHandler<ConfirmMonthlyDocumentsCommand, string> handler,
            CancellationToken cancellationToken) =>
        {
            Result<string> result = await handler.Handle(new ConfirmMonthlyDocumentsCommand(year, month), cancellationToken);
            return result.Match(month => Results.Ok(new { month }), CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Notifications);

        app.MapGet("notifications/monthly-documents/confirmations", async (
            IQueryHandler<GetMonthlyDocumentsConfirmationsQuery, IReadOnlyList<string>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<string>> result = await handler.Handle(new GetMonthlyDocumentsConfirmationsQuery(), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Notifications);
    }
}
