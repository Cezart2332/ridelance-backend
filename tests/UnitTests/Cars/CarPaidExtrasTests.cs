using Application.Abstractions.Authentication;
using Application.Cars;
using Application.Cars.Commands.ApproveCarListing;
using Application.Cars.Queries.GetAllCars;
using Domain.Cars;
using Domain.Payments;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Cars;

/// <summary>
/// Opțiunile plătite ale anunțurilor de flotă: anunțul extra (40 lei/lună) și numărul ascuns
/// (15 lei, o dată per mașină).
/// </summary>
public sealed class CarPaidExtrasTests
{
    [Fact]
    public void Preturile_sunt_cele_stabilite()
    {
        Pricing.PaidExtras.ExtraListingMonthlyBani.ShouldBe(4_000);
        Pricing.PaidExtras.HiddenPlateBani.ShouldBe(1_500);
        StripeCatalog.ExtraListingMonthly.Interval.ShouldBe("month");
        StripeCatalog.HiddenPlate.Interval.ShouldBeNull();
        StripeCatalog.All.ShouldContain(StripeCatalog.ExtraListingMonthly);
        StripeCatalog.All.ShouldContain(StripeCatalog.HiddenPlate);
    }

    /// <summary>Un anunț extra are locul lui: nu consumă din cele incluse în abonament.</summary>
    [Fact]
    public async Task Anunturile_extra_nu_ocupa_locuri_incluse()
    {
        await using ApplicationDbContext db = NewDb();
        var owner = Guid.NewGuid();
        for (int i = 0; i < 3; i++)
        {
            db.Cars.Add(NewCar(owner, ListingStatus.Published, CarListingPaymentStatus.NotRequired));
        }

        db.Cars.Add(NewCar(owner, ListingStatus.Published, CarListingPaymentStatus.Paid));
        await db.SaveChangesAsync();

        (await ListingQuota.GetAsync(db, owner, default)).Used.ShouldBe(3);
    }

    [Fact]
    public async Task Cu_flota_plina_un_anunt_extra_platit_se_publica_la_aprobare()
    {
        await using ApplicationDbContext db = NewDb();
        var admin = new User { Id = Guid.NewGuid(), Email = "admin@ridelance.ro", Role = UserRole.Admin };
        var fleet = new User { Id = Guid.NewGuid(), Email = "flota@ridelance.ro", Role = UserRole.CarPoster };
        db.Users.AddRange(admin, fleet);
        for (int i = 0; i < ListingAllowance.IncludedInFleetPlan; i++)
        {
            db.Cars.Add(NewCar(fleet.Id, ListingStatus.Published, CarListingPaymentStatus.NotRequired));
        }

        Car extra = NewCar(fleet.Id, ListingStatus.Draft, CarListingPaymentStatus.Paid);
        extra.ApprovalStatus = CarApprovalStatus.Pending;
        db.Cars.Add(extra);
        await db.SaveChangesAsync();

        Result result = await new ApproveCarListingCommandHandler(db, new StubUser(admin.Id))
            .Handle(new ApproveCarListingCommand(extra.Id, Approve: true), default);

        result.IsSuccess.ShouldBeTrue();
        extra.ListingStatus.ShouldBe(ListingStatus.Published);
    }

    /// <summary>Numărul plătit ca să fie ascuns nu apare nici în răspunsul public al API-ului.</summary>
    [Fact]
    public void Numarul_ascuns_lipseste_din_raspunsul_public_dar_il_vede_proprietarul()
    {
        Car car = NewCar(Guid.NewGuid(), ListingStatus.Published, CarListingPaymentStatus.NotRequired);
        car.PlateNumber = "B 123 RID";
        car.PlateHidden = true;

        CarDto publicView = CarDtoMapper.ToDto(car, postedByAdmin: false);
        CarDto ownerView = CarDtoMapper.ToDto(car, postedByAdmin: false, revealPlate: true);

        publicView.PlateHidden.ShouldBeTrue();
        publicView.Details!.PlateNumber.ShouldBeNull();
        ownerView.Details!.PlateNumber.ShouldBe("B 123 RID");
    }

    [Fact]
    public void Un_numar_neascuns_ramane_vizibil()
    {
        Car car = NewCar(Guid.NewGuid(), ListingStatus.Published, CarListingPaymentStatus.NotRequired);
        car.PlateNumber = "B 123 RID";

        CarDtoMapper.ToDto(car, postedByAdmin: false).Details!.PlateNumber.ShouldBe("B 123 RID");
    }

    private static Car NewCar(Guid owner, ListingStatus status, CarListingPaymentStatus payment)
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
            ApprovalStatus = CarApprovalStatus.Approved,
            PaymentStatus = payment,
        };
    }

    private static ApplicationDbContext NewDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private sealed class StubUser(Guid id) : IUserContext
    {
        public Guid UserId { get; } = id;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
