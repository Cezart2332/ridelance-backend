using Application.Abstractions.Authentication;
using Application.Cars;
using Application.Cars.Commands.ApproveCarListing;
using Domain.Cars;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Cars;

/// <summary>
/// Anunțurile flotelor sunt incluse în abonament, până la <see cref="ListingAllowance.IncludedInFleetPlan" />
/// active. Nu se mai plătesc separat.
/// </summary>
public sealed class ListingQuotaTests
{
    [Fact]
    public void AnApprovedPublishedListingIsVisibleWithoutPayment()
    {
        var car = new Car
        {
            ListingStatus = ListingStatus.Published,
            ApprovalStatus = CarApprovalStatus.Approved,
            PaymentStatus = CarListingPaymentStatus.Pending,
        };

        car.Active.ShouldBeTrue();
        CarVisibility.IsPublic.Compile()(car).ShouldBeTrue();
    }

    [Fact]
    public async Task OnlyPublishedListingsTakeASlot()
    {
        await using ApplicationDbContext db = NewDb();
        var owner = Guid.NewGuid();
        AddCars(db, owner, ListingStatus.Published, 3);
        AddCars(db, owner, ListingStatus.Draft, 2);
        AddCars(db, owner, ListingStatus.Paused, 1);
        AddCars(db, owner, ListingStatus.Archived, 1);
        AddCars(db, Guid.NewGuid(), ListingStatus.Published, 4);
        await db.SaveChangesAsync();

        ListingQuotaDto quota = await ListingQuota.GetAsync(db, owner, default);

        quota.ShouldBe(new ListingQuotaDto(ListingAllowance.IncludedInFleetPlan, 3, ListingAllowance.IncludedInFleetPlan - 3));
    }

    [Fact]
    public async Task ApprovalPublishesTheListingWhileTheFleetHasRoom()
    {
        await using ApplicationDbContext db = NewDb();
        (Guid admin, Guid fleet) = AddUsers(db);
        AddCars(db, fleet, ListingStatus.Published, ListingAllowance.IncludedInFleetPlan - 1);
        Car pending = AddPending(db, fleet);
        await db.SaveChangesAsync();

        Result result = await new ApproveCarListingCommandHandler(db, new StubUser(admin))
            .Handle(new ApproveCarListingCommand(pending.Id, Approve: true), default);

        result.IsSuccess.ShouldBeTrue();
        pending.ApprovalStatus.ShouldBe(CarApprovalStatus.Approved);
        pending.ListingStatus.ShouldBe(ListingStatus.Published);
    }

    [Fact]
    public async Task ApprovalLeavesTheListingUnpublishedWhenTheFleetIsFull()
    {
        await using ApplicationDbContext db = NewDb();
        (Guid admin, Guid fleet) = AddUsers(db);
        AddCars(db, fleet, ListingStatus.Published, ListingAllowance.IncludedInFleetPlan);
        Car pending = AddPending(db, fleet);
        await db.SaveChangesAsync();

        Result result = await new ApproveCarListingCommandHandler(db, new StubUser(admin))
            .Handle(new ApproveCarListingCommand(pending.Id, Approve: true), default);

        result.IsSuccess.ShouldBeTrue();
        pending.ApprovalStatus.ShouldBe(CarApprovalStatus.Approved);
        pending.ListingStatus.ShouldBe(ListingStatus.Draft);
    }

    private static ApplicationDbContext NewDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private static (Guid Admin, Guid Fleet) AddUsers(ApplicationDbContext db)
    {
        var admin = new User { Id = Guid.NewGuid(), Email = "admin@ridelance.ro", Role = UserRole.Admin };
        var fleet = new User { Id = Guid.NewGuid(), Email = "flota@ridelance.ro", Role = UserRole.CarPoster };
        db.Users.AddRange(admin, fleet);
        return (admin.Id, fleet.Id);
    }

    private static void AddCars(ApplicationDbContext db, Guid owner, ListingStatus status, int count)
    {
        for (int i = 0; i < count; i++)
        {
            db.Cars.Add(NewCar(owner, status, CarApprovalStatus.Approved));
        }
    }

    private static Car AddPending(ApplicationDbContext db, Guid owner)
    {
        Car car = NewCar(owner, ListingStatus.Draft, CarApprovalStatus.Pending);
        db.Cars.Add(car);
        return car;
    }

    private static Car NewCar(Guid owner, ListingStatus status, CarApprovalStatus approval)
    {
        var id = Guid.NewGuid();
        return new Car
        {
            Id = id,
            Brand = "Dacia",
            Model = "Logan",
            Year = 2022,
            Slug = $"dacia-logan-2022-{id:N}",
            PostedByUserId = owner,
            ListingStatus = status,
            ApprovalStatus = approval,
        };
    }

    private sealed class StubUser(Guid id) : IUserContext
    {
        public Guid UserId { get; } = id;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
