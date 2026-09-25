using System.Security.Claims;
using Application.Abstractions.Messaging;
using Application.Payments.CreatePublicServiceCheckout;
using Application.Payments.ServiceOrders;
using Application.PfaRegistrations.Onboarding.CompanyFormation;
using SharedKernel;
using Web.Api.Endpoints.PfaRegistrations;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Payments;

internal sealed class CreatePublicServiceCheckout : IEndpoint
{
    public sealed record Request(
        string ServiceKey,
        string CustomerName,
        string CustomerEmail,
        string CustomerPhone,
        string? SuccessUrl = null,
        string? CancelUrl = null,
        ServiceDossierPayload? Dossier = null);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Public: se cumpără și fără cont. Dacă cererea vine cu token (din dashboard), comanda
        // se leagă de cont. Semnătura se probează cu IP-ul și browserul citite aici, nu de la client.
        app.MapPost("payments/public/service-checkout", async (
            Request request,
            HttpContext httpContext,
            ICommandHandler<CreatePublicServiceCheckoutCommand, string> handler,
            CancellationToken cancellationToken) =>
        {
            string? userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

            var command = new CreatePublicServiceCheckoutCommand(
                request.ServiceKey,
                request.CustomerName,
                request.CustomerEmail,
                request.CustomerPhone,
                request.SuccessUrl,
                request.CancelUrl,
                request.Dossier,
                new SignatureContext(
                    OnboardingCompanyFormation.ClientIpAddress(httpContext),
                    httpContext.Request.Headers.UserAgent.ToString(),
                    null),
                Guid.TryParse(userIdClaim, out Guid userId) ? userId : null);

            Result<string> result = await handler.Handle(command, cancellationToken);

            return result.Match(
                clientSecret => Results.Ok(new { clientSecret }),
                CustomResults.Problem);
        })
        .WithTags(Tags.Payments);
    }
}
