using Application.Abstractions.Messaging;
using Application.Users.Login;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Users;

internal sealed class Login : IEndpoint
{
    public sealed record Request(string Email, string Password);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/login", async (
            Request request,
            ICommandHandler<LoginUserCommand, LoginResponse> handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var command = new LoginUserCommand(request.Email, request.Password);

            Result<LoginResponse> result = await handler.Handle(command, cancellationToken);

            if (result.IsFailure)
            {
                return CustomResults.Problem(result);
            }

            // Set refresh token as HTTP-only cookie
            httpContext.Response.Cookies.Append("refreshToken", result.Value.RefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(7),
                Path = "/"
            });

            // Return only the access token, role, and userId in the response body (for Redux)
            return Results.Ok(new
            {
                accessToken = result.Value.AccessToken,
                role = result.Value.Role,
                userId = result.Value.UserId
            });
        })
        .WithTags(Tags.Users);
    }
}
