using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations.Onboarding;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

/// <summary>
/// Actele dosarelor de la pașii 4 (ARR) și 6 (copie conformă), din admin: ce lipsește, ce așteaptă
/// validarea și butonul „Validează documentele pentru dosar”, care îl lasă pe client să genereze.
/// </summary>
internal sealed class OnboardingDossier : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("admin/onboarding/{id:guid}/steps/{step}/dossier", async (
            Guid id,
            string step,
            IQueryHandler<GetDossierReadinessQuery, DossierReadinessResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<DossierReadinessResponse> result = await handler.Handle(new GetDossierReadinessQuery(id, step), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("admin/onboarding/{id:guid}/steps/{step}/dossier/validate", async (
            Guid id,
            string step,
            IUserContext userContext,
            ICommandHandler<ValidateDossierDocumentsCommand, DossierReadinessResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<DossierReadinessResponse> result =
                await handler.Handle(new ValidateDossierDocumentsCommand(id, step, userContext.UserId), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .WithTags(Tags.PfaRegistrations);
    }
}
