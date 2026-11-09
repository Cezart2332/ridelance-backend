using Application.Abstractions.Messaging;
using Application.PfaRegistrations.Onboarding.CompanyFormation;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

/// <summary>
/// Acordul proprietarului sediului, semnat din linkul primit pe email. Rute anonime: proprietarul
/// n-are cont, iar tokenul din link e autentificarea (hash-uit la noi, 7 zile, o singură folosire).
/// </summary>
internal sealed class OwnerConsentSigning : IEndpoint
{
    internal sealed record SignRequest(string SignatureImage);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("owner-consent/{token}", async (
            string token,
            IQueryHandler<GetOwnerConsentQuery, OwnerConsentRequestDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<OwnerConsentRequestDto> result = await handler.Handle(new GetOwnerConsentQuery(token), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .AllowAnonymous()
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("owner-consent/{token}", async (
            string token,
            SignRequest body,
            HttpContext httpContext,
            ICommandHandler<SignOwnerConsentCommand> handler,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(body);

            // IP-ul și dispozitivul se citesc din cerere, nu din corpul ei.
            Result result = await handler.Handle(
                new SignOwnerConsentCommand(
                    token,
                    body.SignatureImage,
                    httpContext.Connection.RemoteIpAddress?.ToString(),
                    httpContext.Request.Headers.UserAgent.ToString()),
                cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .AllowAnonymous()
        .WithTags(Tags.PfaRegistrations);
    }
}
