using Application.Abstractions.Messaging;
using Application.Users.DeleteAccount;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Users;

internal sealed class DeleteAccount : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/me/delete", async (
            DeleteAccountRequest request,
            ICommandHandler<DeleteOwnAccountCommand> handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new DeleteOwnAccountCommand(request.Password), cancellationToken);

            if (result.IsSuccess)
            {
                httpContext.Response.Cookies.Delete("refreshToken", new CookieOptions { Path = "/" });
            }

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Users);
    }

    internal sealed record DeleteAccountRequest(string Password);
}
