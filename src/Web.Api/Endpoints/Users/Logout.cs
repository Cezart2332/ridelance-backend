using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Users.Logout;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Users;

internal sealed class LogoutEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/logout", async (
            IUserContext userContext,
            ICommandHandler<LogoutCommand> handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var command = new LogoutCommand(userContext.UserId);

            Result result = await handler.Handle(command, cancellationToken);

            // Always clear the cookie
            httpContext.Response.Cookies.Delete("refreshToken", new CookieOptions
            {
                Path = "/"
            });

            if (result.IsFailure)
            {
                return CustomResults.Problem(result);
            }

            return Results.Ok();
        })
        .RequireAuthorization()
        .WithTags(Tags.Users);
    }
}
