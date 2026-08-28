using Application.Abstractions.Messaging;
using Application.Rentals.Signing;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Companies;

/// <summary>
/// Semnarea din email, fără cont.
/// </summary>
/// <remarks>
/// Singurele rute anonime din tot fluxul de flotă. Autentificarea e tokenul din link: neghicibil,
/// păstrat hash-uit la noi, valabil șapte zile și consumabil o singură dată.
///
/// Descărcarea documentului merge tot prin token, nu prin ruta obișnuită cu JWT: chiriașul trebuie
/// să citească ce semnează, iar cont nu are.
/// </remarks>
internal sealed class Signing : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("signing/{token}", async (
            string token,
            IQueryHandler<GetSignatureRequestQuery, SignatureRequestDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<SignatureRequestDto> result = await handler.Handle(
                new GetSignatureRequestQuery(token), cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .AllowAnonymous()
        .WithTags(Tags.Companies);

        app.MapPost("signing/{token}", async (
            string token,
            SignRequest body,
            HttpContext httpContext,
            ICommandHandler<SignDocumentCommand> handler,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(body);

            // IP-ul și user-agentul se citesc din cerere, nu din corpul ei: cine semnează nu-și
            // poate proba singur contextul.
            var signingContext = new SigningContext(
                httpContext.Connection.RemoteIpAddress?.ToString(),
                httpContext.Request.Headers.UserAgent.ToString());

            Result result = await handler.Handle(
                new SignDocumentCommand(token, body.SignatureImage, signingContext),
                cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .AllowAnonymous()
        .WithTags(Tags.Companies);
    }

    internal sealed record SignRequest(string SignatureImage);
}

internal sealed class SigningDocument : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("signing/{token}/document", async (
            string token,
            IQueryHandler<DownloadSignatureDocumentQuery, SignatureFileDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<SignatureFileDto> result = await handler.Handle(
                new DownloadSignatureDocumentQuery(token), cancellationToken);

            return result.IsFailure
                ? CustomResults.Problem(result)
                : Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
        })
        .AllowAnonymous()
        .WithTags(Tags.Companies);
    }
}
