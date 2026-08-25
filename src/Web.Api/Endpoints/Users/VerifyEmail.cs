using Application.Abstractions.Messaging;
using Application.Users.ResendVerification;
using Application.Users.VerifyEmail;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Users;

/// <summary>
/// Confirmarea adresei de email.
/// </summary>
/// <remarks>
/// Anonim, ca și înregistrarea: se apelează imediat după creare, înainte ca cineva să aibă un
/// token. Codul e singura dovadă cerută.
/// </remarks>
internal sealed class VerifyEmail : IEndpoint
{
    public sealed record Request(string Email, string Code);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/verify-email", async (
            Request request,
            ICommandHandler<VerifyEmailCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new VerifyEmailCommand(request.Email, request.Code), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .WithTags(Tags.Users);
    }
}

internal sealed class ResendVerification : IEndpoint
{
    public sealed record Request(string Email);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/resend-verification", async (
            Request request,
            ICommandHandler<ResendVerificationCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new ResendVerificationCommand(request.Email), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .WithTags(Tags.Users);
    }
}
