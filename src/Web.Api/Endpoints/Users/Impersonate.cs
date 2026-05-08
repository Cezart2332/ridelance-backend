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
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var command = new ImpersonateUserCommand(userId);

            Result<LoginResponse> result = await handler.Handle(command, cancellationToken);

            if (result.IsFailure)
            {
                return CustomResults.Problem(result);
            }

            // Set refresh token as HTTP-only cookie for the target user
            httpContext.Response.Cookies.Append("refreshToken", result.Value.RefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddDays(7),
                Path = "/users"
            });

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
