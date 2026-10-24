using Application.Abstractions.Messaging;
using Application.Users.PasswordReset;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Users;

/// <summary>
/// Resetarea parolei uitate: un cod pe email, apoi parola nouă cu codul. Anonim — cine și-a uitat
/// parola n-are sesiune.
/// </summary>
internal sealed class PasswordReset : IEndpoint
{
    public sealed record ForgotRequest(string Email);

    public sealed record ResetRequest(string Email, string Code, string NewPassword);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/forgot-password", async (
            ForgotRequest request,
            ICommandHandler<RequestPasswordResetCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new RequestPasswordResetCommand(request.Email), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .AllowAnonymous()
        .WithTags(Tags.Users);

        app.MapPost("users/reset-password", async (
            ResetRequest request,
            ICommandHandler<ResetPasswordCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(
                new ResetPasswordCommand(request.Email, request.Code, request.NewPassword), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .AllowAnonymous()
        .WithTags(Tags.Users);
    }
}
