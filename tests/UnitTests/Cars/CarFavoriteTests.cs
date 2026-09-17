using Application.Cars.Favorites;
using Domain.Cars;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Cars;

/// <summary>
/// Favoritele: cele din browser se mută în cont la logare, fără dubluri și fără anunțuri pe care
/// omul nu le mai poate vedea.
/// </summary>
public sealed class CarFavoriteTests
{
    [Fact]
    public async Task MergeKeepsOnlyPublicCarsAndSkipsDuplicates()
    {
        await using ApplicationDbContext db = NewDb();
        var user = Guid.NewGuid();
        Car saved = AddCar(db, ListingStatus.Published);
        Car fresh = AddCar(db, ListingStatus.Published);
        Car withdrawn = AddCar(db, ListingStatus.Paused);
        db.CarFavorites.Add(new CarFavorite { Id = Guid.NewGuid(), UserId = user, CarId = saved.Id });
        await db.SaveChangesAsync();

        Result<List<Guid>> result = await new MergeCarFavoritesCommandHandler(db).Handle(
            new MergeCarFavoritesCommand(user, [saved.Id, fresh.Id, fresh.Id, withdrawn.Id, Guid.NewGuid()]),
            default);

        result.Value.ShouldBe([saved.Id, fresh.Id], ignoreOrder: true);
        (await db.CarFavorites.CountAsync(f => f.UserId == user)).ShouldBe(2);
    }

    [Fact]
    public async Task AddIsIdempotentAndRemoveOfMissingIsNotAnError()
    {
        await using ApplicationDbContext db = NewDb();
        var user = Guid.NewGuid();
        Car car = AddCar(db, ListingStatus.Published);
        await db.SaveChangesAsync();

        var add = new AddCarFavoriteCommandHandler(db);
        (await add.Handle(new AddCarFavoriteCommand(user, car.Id), default)).IsSuccess.ShouldBeTrue();
        (await add.Handle(new AddCarFavoriteCommand(user, car.Id), default)).IsSuccess.ShouldBeTrue();
        (await db.CarFavorites.CountAsync()).ShouldBe(1);

        var remove = new RemoveCarFavoriteCommandHandler(db);
        (await remove.Handle(new RemoveCarFavoriteCommand(user, car.Id), default)).IsSuccess.ShouldBeTrue();
        (await remove.Handle(new RemoveCarFavoriteCommand(user, car.Id), default)).IsSuccess.ShouldBeTrue();
        (await db.CarFavorites.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AddRejectsACarThatIsNotPublic()
    {
        await using ApplicationDbContext db = NewDb();
        Car draft = AddCar(db, ListingStatus.Draft);
        await db.SaveChangesAsync();

        Result result = await new AddCarFavoriteCommandHandler(db)
            .Handle(new AddCarFavoriteCommand(Guid.NewGuid(), draft.Id), default);

        result.IsFailure.ShouldBeTrue();
    }

    private static ApplicationDbContext NewDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private static Car AddCar(ApplicationDbContext db, ListingStatus status)
    {
        var id = Guid.NewGuid();
        var car = new Car
        {
            Id = id,
            Brand = "Dacia",
            Model = "Logan",
            Year = 2022,
            Slug = $"dacia-logan-2022-{id:N}",
            ListingStatus = status,
            ApprovalStatus = CarApprovalStatus.Approved,
        };
        db.Cars.Add(car);
        return car;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
