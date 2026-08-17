using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations.Onboarding.DevTools;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

/// <summary>
/// Uneltele de dezvoltare pentru onboarding (spec fix-uri §13).
///
/// State machine-ul e server-side, deci saltul între pași se autorizează AICI, nu în UI. Când
/// poarta nu trece, ruta răspunde <b>404</b>: un 403 ar confirma că endpoint-ul există.
///
/// Nu sunt înregistrate deloc când mediul e Production sau flagul e stins — vezi
/// <see cref="OnboardingDevToolsGate"/>. Rămâne totuși verificarea per-cerere: configurația se
/// poate schimba la cald, iar un endpoint înregistrat la pornire nu are voie să rămână deschis.
/// </summary>
internal sealed class OnboardingDevTools : IEndpoint
{
    public sealed record JumpRequest(string TargetStepId);

    public sealed record CompleteRequest(string StepId, bool UseMockData = true);

    public sealed record ResetRequest(string Scope, string? TargetId);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("dev/onboarding/{onboardingId:guid}/jump", async (
            Guid onboardingId,
            JumpRequest request,
            IUserContext userContext,
            OnboardingDevToolsGate gate,
            ICommandHandler<JumpToOnboardingStepCommand> handler,
            CancellationToken cancellationToken) =>
        {
            if (!await gate.IsAllowedAsync(userContext.UserId, cancellationToken))
            {
                return Results.NotFound();
            }

            Result result = await handler.Handle(
                new JumpToOnboardingStepCommand(onboardingId, userContext.UserId, request.TargetStepId),
                cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("dev/onboarding/{onboardingId:guid}/complete", async (
            Guid onboardingId,
            CompleteRequest request,
            IUserContext userContext,
            OnboardingDevToolsGate gate,
            ICommandHandler<CompleteOnboardingStepCommand> handler,
            CancellationToken cancellationToken) =>
        {
            if (!await gate.IsAllowedAsync(userContext.UserId, cancellationToken))
            {
                return Results.NotFound();
            }

            Result result = await handler.Handle(
                new CompleteOnboardingStepCommand(
                    onboardingId, userContext.UserId, request.StepId, request.UseMockData),
                cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("dev/onboarding/{onboardingId:guid}/reset", async (
            Guid onboardingId,
            ResetRequest request,
            IUserContext userContext,
            OnboardingDevToolsGate gate,
            ICommandHandler<ResetOnboardingCommand> handler,
            CancellationToken cancellationToken) =>
        {
            if (!await gate.IsAllowedAsync(userContext.UserId, cancellationToken))
            {
                return Results.NotFound();
            }

            Result result = await handler.Handle(
                new ResetOnboardingCommand(onboardingId, userContext.UserId, request.Scope, request.TargetId),
                cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);
    }
}
