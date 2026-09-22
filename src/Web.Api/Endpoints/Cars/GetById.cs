using Application.Abstractions.Messaging;
using Application.Cars.Queries.GetAllCars;
using Application.Cars.Queries.GetCarById;
using Application.Cars.Queries.GetCarBySlug;
using System.Security.Claims;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Cars;

internal sealed class GetById : IEndpoint
{
    /// <summary>Cine cere anunțul, dacă e autentificat. Endpointul e public, deci poate lipsi.</summary>
    internal static Guid? ViewerId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : null;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("cars/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            IQueryHandler<GetCarByIdQuery, CarDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<CarDto> result = await handler.Handle(new GetCarByIdQuery(id, ViewerId(user)), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .AllowAnonymous()
        .WithTags(Tags.Cars);
    }
}

/// <summary>Aceeași mașină, cerută după slug-ul din URL-ul public.</summary>
internal sealed class GetBySlug : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("cars/by-slug/{slug}", async (
            string slug,
            IQueryHandler<GetCarBySlugQuery, CarDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<CarDto> result = await handler.Handle(new GetCarBySlugQuery(slug), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .AllowAnonymous()
        .WithTags(Tags.Cars);
    }
}
