using Application.Companies.Onboarding;
using Infrastructure.Authorization;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Companies;

internal sealed class FleetOnboardingEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("admin/fleet-bcr", async (FleetBcrService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(ct))).RequireAuthorization()
            .HasPermission(Permissions.ManageDiscounts);
        app.MapPost("admin/fleet-bcr/{userId:guid}/confirm", async (Guid userId, FleetBcrService service, CancellationToken ct) =>
            (await service.ConfirmAsync(userId, ct)).Match(Results.NoContent, CustomResults.Problem)).RequireAuthorization()
            .HasPermission(Permissions.ManageDiscounts);
        RouteGroupBuilder group = app.MapGroup("fleet-onboarding").RequireAuthorization().WithTags("Fleet onboarding");
        group.MapGet("", async (FleetOnboardingService service, CancellationToken ct) =>
            (await service.GetAsync(ct)).Match(Results.Ok, CustomResults.Problem));
        group.MapPut("", async (FleetOnboardingInput input, FleetOnboardingService service, CancellationToken ct) =>
            (await service.SaveAsync(input, ct)).Match(Results.Ok, CustomResults.Problem));
        group.MapPost("checkout", async (FleetOnboardingService service, CancellationToken ct) =>
            (await service.CheckoutAsync(ct)).Match(secret => Results.Ok(new { clientSecret = secret }), CustomResults.Problem));
        group.MapDelete("checkout", async (FleetOnboardingService service, CancellationToken ct) =>
            (await service.CancelCheckoutAsync(ct)).Match(Results.Ok, CustomResults.Problem));
    }
}
