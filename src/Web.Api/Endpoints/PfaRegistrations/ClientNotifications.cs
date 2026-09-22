using Application.Abstractions.Messaging;
using Application.PfaRegistrations.ClientNotifications;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

/// <summary>Contabilul trimite o notificare (în aplicație și push) clientului PFA.</summary>
internal sealed class ClientNotifications : IEndpoint
{
    public sealed record SendClientNotificationRequest(string Text, string? Destination);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("pfa-registrations/{id:guid}/client-notifications", async (
            Guid id,
            SendClientNotificationRequest request,
            ICommandHandler<SendClientNotificationCommand, SendClientNotificationResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new SendClientNotificationCommand(id, request.Text, request.Destination);
            Result<SendClientNotificationResponse> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageClientIncome)
        .WithTags(Tags.PfaRegistrations);
    }
}
