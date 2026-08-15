using Application.Abstractions.Messaging;
using Application.Users.ChangePassword;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Users;

internal sealed class ChangePassword : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/change-password", async (
            ChangePasswordRequest request,
            ICommandHandler<ChangePasswordCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(
                new ChangePasswordCommand(request.CurrentPassword, request.NewPassword),
                cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Users);
    }

    internal sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
}
