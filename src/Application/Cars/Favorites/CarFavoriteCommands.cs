using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Favorites;

/// <summary>Id-urile mașinilor de la favoritele utilizatorului, cele mai noi primele.</summary>
public sealed record GetCarFavoritesQuery(Guid UserId) : IQuery<List<Guid>>;

/// <summary>Adaugă o mașină la favorite. Idempotent: una deja salvată rămâne salvată.</summary>
public sealed record AddCarFavoriteCommand(Guid UserId, Guid CarId) : ICommand;

/// <summary>Scoate o mașină de la favorite. Idempotent: una care nu era acolo nu e o eroare.</summary>
public sealed record RemoveCarFavoriteCommand(Guid UserId, Guid CarId) : ICommand;

/// <summary>
/// Mută în cont favoritele salvate în browser înainte de logare. Întoarce lista completă, ca
/// frontendul să nu mai facă o a doua cerere după mutare.
/// </summary>
public sealed record MergeCarFavoritesCommand(Guid UserId, IReadOnlyList<Guid> CarIds) : ICommand<List<Guid>>;

internal static class CarFavoriteRules
{
    /// <summary>
    /// Cât acceptă o singură mutare. Un browser nu strânge sute de favorite, iar fără limită
    /// endpointul ar fi o cale ieftină de a scrie mii de rânduri.
    /// </summary>
    public const int MaxMerge = 200;

    public static Task<List<Guid>> ListAsync(IApplicationDbContext context, Guid userId, CancellationToken ct) =>
        context.CarFavorites
            .AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => f.CarId)
            .ToListAsync(ct);
}

internal sealed class GetCarFavoritesQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetCarFavoritesQuery, List<Guid>>
{
    public async Task<Result<List<Guid>>> Handle(GetCarFavoritesQuery query, CancellationToken cancellationToken) =>
        await CarFavoriteRules.ListAsync(context, query.UserId, cancellationToken);
}

internal sealed class AddCarFavoriteCommandHandler(IApplicationDbContext context)
    : ICommandHandler<AddCarFavoriteCommand>
{
    public async Task<Result> Handle(AddCarFavoriteCommand command, CancellationToken cancellationToken)
    {
        // Doar anunțurile publice: o mașină pe care omul n-o poate vedea nu are ce căuta la favorite.
        bool carIsPublic = await context.Cars
            .AsNoTracking()
            .Where(c => c.Id == command.CarId)
            .Where(CarVisibility.IsPublic)
            .AnyAsync(cancellationToken);

        if (!carIsPublic)
        {
            return Result.Failure(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        bool exists = await context.CarFavorites
            .AnyAsync(f => f.UserId == command.UserId && f.CarId == command.CarId, cancellationToken);

        if (exists)
        {
            return Result.Success();
        }

        context.CarFavorites.Add(new CarFavorite
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId,
            CarId = command.CarId,
            CreatedAtUtc = DateTime.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class RemoveCarFavoriteCommandHandler(IApplicationDbContext context)
    : ICommandHandler<RemoveCarFavoriteCommand>
{
    public async Task<Result> Handle(RemoveCarFavoriteCommand command, CancellationToken cancellationToken)
    {
        CarFavorite? favorite = await context.CarFavorites
            .FirstOrDefaultAsync(f => f.UserId == command.UserId && f.CarId == command.CarId, cancellationToken);

        if (favorite is not null)
        {
            context.CarFavorites.Remove(favorite);
            await context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}

internal sealed class MergeCarFavoritesCommandHandler(IApplicationDbContext context)
    : ICommandHandler<MergeCarFavoritesCommand, List<Guid>>
{
    public async Task<Result<List<Guid>>> Handle(MergeCarFavoritesCommand command, CancellationToken cancellationToken)
    {
        var requested = command.CarIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(CarFavoriteRules.MaxMerge)
            .ToList();

        if (requested.Count > 0)
        {
            // Id-urile vin din browser: pot fi mașini șterse sau retrase între timp. Păstrăm doar
            // anunțurile publice care nu sunt deja în cont.
            List<Guid> publicIds = await context.Cars
                .AsNoTracking()
                .Where(c => requested.Contains(c.Id))
                .Where(CarVisibility.IsPublic)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);

            var alreadySaved = (await context.CarFavorites
                    .AsNoTracking()
                    .Where(f => f.UserId == command.UserId && publicIds.Contains(f.CarId))
                    .Select(f => f.CarId)
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            DateTime now = DateTime.UtcNow;
            foreach (Guid carId in publicIds.Where(id => !alreadySaved.Contains(id)))
            {
                context.CarFavorites.Add(new CarFavorite
                {
                    Id = Guid.NewGuid(),
                    UserId = command.UserId,
                    CarId = carId,
                    CreatedAtUtc = now,
                });
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        return await CarFavoriteRules.ListAsync(context, command.UserId, cancellationToken);
    }
}
