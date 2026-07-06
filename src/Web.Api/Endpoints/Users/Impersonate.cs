using Application.Abstractions.Messaging;
using Application.Users.Impersonate;
using Application.Users.Login;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Users;

internal sealed class Impersonate : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/impersonate/{userId:guid}", async (
            Guid userId,
            ICommandHandler<ImpersonateUserCommand, LoginResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new ImpersonateUserCommand(userId);

            Result<LoginResponse> result = await handler.Handle(command, cancellationToken);

            if (result.IsFailure)
            {
                return CustomResults.Problem(result);
            }

            // The refresh token cookie is intentionally left untouched: it stays
            // the admin's, so the admin can always return to their own session.

            // Return access token, role, and userId
            return Results.Ok(new
            {
                accessToken = result.Value.AccessToken,
                role = result.Value.Role,
                userId = result.Value.UserId
            });
        })
        .RequireAuthorization()
        .WithTags(Tags.Users);
    }
}
