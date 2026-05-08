using Application.Abstractions.Messaging;
using Application.Users.InviteContabil;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Users;

internal sealed class InviteContabil : IEndpoint
{
    public sealed record Request(string FullName, string Email);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/invite-contabil", async (
            Request request,
            ICommandHandler<InviteContabilCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new InviteContabilCommand(request.FullName, request.Email);

            Result<Guid> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Users);
    }
}
