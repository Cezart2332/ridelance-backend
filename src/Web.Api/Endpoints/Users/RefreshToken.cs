using Microsoft.AspNetCore.Mvc;
using Application.Abstractions.Messaging;
using Application.Users.RefreshToken;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Users;

internal sealed class RefreshTokenEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/refresh-token", async (
            HttpContext httpContext,
            [FromServices] ICommandHandler<RefreshTokenCommand, RefreshTokenResponse> handler,
            CancellationToken cancellationToken) =>
        {
            string? refreshToken = httpContext.Request.Cookies["refreshToken"];

            if (string.IsNullOrEmpty(refreshToken))
            {
                return Results.Unauthorized();
            }

            var command = new RefreshTokenCommand(refreshToken);

            Result<RefreshTokenResponse> result = await handler.Handle(command, cancellationToken);

            if (result.IsFailure)
            {
                // Clear the invalid cookie
                httpContext.Response.Cookies.Delete("refreshToken", new CookieOptions
                {
                    Path = "/"
                });

                return CustomResults.Problem(result);
            }

            // Rotate the refresh token cookie
            httpContext.Response.Cookies.Append("refreshToken", result.Value.RefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(7),
                Path = "/"
            });

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
