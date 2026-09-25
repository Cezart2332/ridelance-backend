using Application.Abstractions.Messaging;
using Application.Payments.ServiceOrders;
using SharedKernel;
using Web.Api.Endpoints.PfaRegistrations;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Payments;

/// <summary>
/// Citește buletinul din formularul serviciilor de pe site, pentru precompletarea datelor.
///
/// Public, ca formularul: cine cumpără de pe site n-are cont. Fișierul nu se salvează; plafonul
/// pe IP îl ține handlerul, fiindcă fiecare citire e un apel plătit la model.
/// </summary>
internal sealed class ScanServiceIdentityCard : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("payments/public/scan-identity-card", async (
            IFormFile file,
            HttpContext httpContext,
            ICommandHandler<ScanIdentityCardCommand, IdentityCardScan> handler,
            CancellationToken cancellationToken) =>
        {
            await using Stream stream = file.OpenReadStream();

            Result<IdentityCardScan> result = await handler.Handle(
                new ScanIdentityCardCommand(
                    file.FileName,
                    stream,
                    file.ContentType,
                    file.Length,
                    OnboardingCompanyFormation.ClientIpAddress(httpContext) ?? "necunoscut"),
                cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .DisableAntiforgery()
        .WithTags(Tags.Payments);
    }
}
