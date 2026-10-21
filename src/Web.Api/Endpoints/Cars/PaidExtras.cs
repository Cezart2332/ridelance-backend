using Application.Abstractions.Messaging;
using Application.Cars.Commands.PaidExtras;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Cars;

/// <summary>Opțiunile plătite ale unui anunț de flotă: anunț extra și număr ascuns.</summary>
internal sealed class PaidExtras : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("cars/{id:guid}/extra-listing/checkout", async (
            Guid id,
            CarCheckoutRequest? request,
            ICommandHandler<CreateExtraListingCheckoutCommand, string> handler,
            CancellationToken cancellationToken) =>
        {
            Result<string> result = await handler.Handle(new CreateExtraListingCheckoutCommand(id, request?.DiscountCode), cancellationToken);
            return result.Match(secret => Results.Ok(new { clientSecret = secret }), CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Cars);

        app.MapPost("cars/{id:guid}/extra-listing/cancel", async (
            Guid id,
            ICommandHandler<CancelExtraListingCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new CancelExtraListingCommand(id), cancellationToken);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Cars);

        app.MapPost("cars/{id:guid}/hidden-plate/checkout", async (
            Guid id,
            CarCheckoutRequest? request,
            ICommandHandler<CreateHiddenPlateCheckoutCommand, string> handler,
            CancellationToken cancellationToken) =>
        {
            Result<string> result = await handler.Handle(new CreateHiddenPlateCheckoutCommand(id, request?.DiscountCode), cancellationToken);
            return result.Match(secret => Results.Ok(new { clientSecret = secret }), CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Cars);
    }
}

/// <summary>Corpul opțional al unui checkout per mașină: codul de reducere scris în dialog.</summary>
internal sealed record CarCheckoutRequest(string? DiscountCode);
