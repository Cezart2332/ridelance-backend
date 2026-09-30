using Application.Abstractions.Messaging;
using Application.Eldrive;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Companies;

/// <summary>
/// Eldrive: clientul se conectează din Conexiuni (invitație în contul de partener RIDElance),
/// iar adminul ține evidența și scoate invitațiile celor care renunță la abonament.
/// </summary>
internal sealed class EldriveEndpoints : IEndpoint
{
    public sealed record ConnectRequest(string Email);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder client = app.MapGroup("connections/eldrive")
            .RequireAuthorization()
            .WithTags(Tags.Companies);

        client.MapGet(string.Empty, async (
            IQueryHandler<GetEldriveConnectionQuery, EldriveConnectionDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<EldriveConnectionDto> result = await handler.Handle(new GetEldriveConnectionQuery(), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        client.MapPost(string.Empty, async (
            ConnectRequest request,
            ICommandHandler<ConnectEldriveCommand, EldriveConnectionDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<EldriveConnectionDto> result = await handler.Handle(new ConnectEldriveCommand(request.Email), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        RouteGroupBuilder admin = app.MapGroup("admin/eldrive/invites")
            .RequireAuthorization()
            .WithTags(Tags.Admin);

        admin.MapGet(string.Empty, async (
            IQueryHandler<GetEldriveInvitesQuery, List<EldriveInviteAdminDto>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<List<EldriveInviteAdminDto>> result = await handler.Handle(new GetEldriveInvitesQuery(), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .HasPermission(Permissions.ManagePfaRegistrations);

        admin.MapDelete("{id:guid}", async (
            Guid id,
            ICommandHandler<RemoveEldriveInviteCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new RemoveEldriveInviteCommand(id), cancellationToken);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .HasPermission(Permissions.ManagePfaRegistrations);
    }
}
