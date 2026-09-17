using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Cars.Favorites;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Cars;

/// <summary>
/// Favoritele utilizatorului logat. Oricine are cont își poate salva mașini, indiferent de rol;
/// fără cont, favoritele stau în browser și se mută aici la logare (<c>favorites/merge</c>).
/// </summary>
internal sealed class Favorites : IEndpoint
{
    public sealed record MergeRequest(List<Guid> CarIds);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("cars/favorites", async (
            IUserContext userContext,
            IQueryHandler<GetCarFavoritesQuery, List<Guid>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<List<Guid>> result = await handler.Handle(new GetCarFavoritesQuery(userContext.UserId), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Cars);

        app.MapPut("cars/{id:guid}/favorite", async (
            Guid id,
            IUserContext userContext,
            ICommandHandler<AddCarFavoriteCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new AddCarFavoriteCommand(userContext.UserId, id), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags(Tags.Cars);

        app.MapDelete("cars/{id:guid}/favorite", async (
            Guid id,
            IUserContext userContext,
            ICommandHandler<RemoveCarFavoriteCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new RemoveCarFavoriteCommand(userContext.UserId, id), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags(Tags.Cars);

        app.MapPost("cars/favorites/merge", async (
            MergeRequest request,
            IUserContext userContext,
            ICommandHandler<MergeCarFavoritesCommand, List<Guid>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<List<Guid>> result = await handler.Handle(
                new MergeCarFavoritesCommand(userContext.UserId, request.CarIds ?? []),
                cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Cars);
    }
}
