using Application.Abstractions.Authentication;
using Application.Abstractions.Services;
using Application.Companies.Commands.UpdateCompanyProfile;
using Application.Rentals;
using Application.Rentals.Checks;
using Application.Rentals.Commands.CreateRental;
using Application.Rentals.Commands.FillDocumentFields;
using Application.Rentals.Commands.SaveRentalDefaults;
using Application.Rentals.Commands.UpdateRental;
using Application.Rentals.Documents;
using Application.Rentals.Queries.GetRentalDefaults;
using Domain.Cars;
using Domain.Companies;
using Domain.Rentals;
using Domain.Users;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Rentals;

/// <summary>
/// Câmpurile noi ale contractului, de la formular până în baza de date.
/// </summary>
/// <remarks>
/// Ce prind testele: termenii flotei copiați pe închiriere (nu referiți), un formular vechi care
/// nu știe de câmpurile noi și nu are voie să le șteargă, și formularul „completează ce lipsește”
/// care trebuie să poată umple tot ce cere verificatorul.
/// </remarks>
public sealed class RentalContractFieldsHandlerTests
{
    [Fact]
    public async Task Creating_a_rental_copies_the_fleet_terms_it_was_not_given()
    {
        (ApplicationDbContext db, Guid owner, Car car) = await Seed();
        db.FleetRentalDefaults.Add(new FleetRentalDefaults
        {
            Id = Guid.NewGuid(),
            OwnerUserId = owner,
            PaymentDueDay = DayOfWeek.Friday,
            PaymentMethod = RentalPaymentMethod.Cash,
            LatePenaltyPercentPerDay = 1m,
            FranchiseBani = 200_000,
            NoticeDays = 30,
            UnavailabilityRule = RentalUnavailabilityRule.FullyDue,
        });
        await db.SaveChangesAsync();

        // Formularul trimite doar două dintre termeni; restul vin din setările flotei.
        Result<Guid> result = await Create(db, owner, NewRental(car.Id) with
        {
            PaymentDueDay = null,
            PaymentMethod = "bankTransfer",
            FranchiseBani = null,
            NoticeDays = 7,
            UnavailabilityRule = null,
        });

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Description : null);
        Rental rental = await db.Rentals.SingleAsync();
        rental.PaymentDueDay.ShouldBe(DayOfWeek.Friday);
        rental.PaymentMethod.ShouldBe(RentalPaymentMethod.BankTransfer); // trimis bate setarea
        rental.LatePenaltyPercentPerDay.ShouldBe(1m);
        rental.FranchiseBani.ShouldBe(200_000);
        rental.NoticeDays.ShouldBe(7);
        rental.UnavailabilityRule.ShouldBe(RentalUnavailabilityRule.FullyDue);
        rental.KeysCount.ShouldBe(2);
        rental.HandoverDocuments.ShouldBe([HandoverDocumentCodes.RegistrationCertificate, HandoverDocumentCodes.Rca]);
        rental.AdditionalDriverName.ShouldBe("Mihai Pop");
    }

    [Fact]
    public async Task An_unknown_payment_day_is_rejected_not_dropped()
    {
        (ApplicationDbContext db, Guid owner, Car car) = await Seed();

        Result<Guid> result = await Create(db, owner, NewRental(car.Id) with { PaymentDueDay = "Lunea" });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Rental.Invalid.paymentDueDay");
        (await db.Rentals.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task The_srl_tenant_keeps_its_representative_an_individual_never_gets_one()
    {
        (ApplicationDbContext db, Guid owner, Car car) = await Seed();

        await Create(db, owner, NewRental(car.Id) with
        {
            Tenant = new NewTenantInput("Chiriaș SRL", "Srl", null, null, null, "RO1", "J12/1/2020", "Cluj", null, null, null, "Maria Ionescu"),
        });

        (await db.Tenants.SingleAsync()).RepresentativeName.ShouldBe("Maria Ionescu");
    }

    [Fact]
    public async Task An_old_edit_form_does_not_erase_the_new_contract_fields()
    {
        (ApplicationDbContext db, Guid owner, Car car) = await Seed();
        await Create(db, owner, NewRental(car.Id));
        Rental rental = await db.Rentals.SingleAsync();

        // Formularul de corectare de dinainte: nu trimite niciunul dintre câmpurile noi.
        Result result = await new UpdateRentalCommandHandler(db, new User(owner)).Handle(
            new UpdateRentalCommand(rental.Id, rental.StartAtUtc, rental.EndAtUtc, 50_000, 100_000, 0, false, null, 0, null, "Plin", 45_000, ["Chei"], null, null),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        rental.WeeklyRentBani.ShouldBe(50_000);
        rental.PaymentDueDay.ShouldBe(DayOfWeek.Monday);
        rental.NoticeDays.ShouldBe(15);
        rental.AdditionalDriverName.ShouldBe("Mihai Pop");
        rental.HandoverDocuments.Count.ShouldBe(2);

        // Un text gol șterge: șoferul suplimentar a renunțat.
        await new UpdateRentalCommandHandler(db, new User(owner)).Handle(
            new UpdateRentalCommand(rental.Id, rental.StartAtUtc, rental.EndAtUtc, 50_000, 100_000, 0, false, null, 0, null, "Plin", 45_000, ["Chei"], null, null,
                AdditionalDriverName: "", PaymentDueDay: "Tuesday"),
            CancellationToken.None);

        rental.AdditionalDriverName.ShouldBeNull();
        rental.PaymentDueDay.ShouldBe(DayOfWeek.Tuesday);
    }

    [Fact]
    public async Task The_fill_form_can_complete_everything_the_contract_asks_for()
    {
        (ApplicationDbContext db, Guid owner, Car car) = await Seed();
        car.Year = 0;
        db.CompanyProfiles.Add(new CompanyProfile
        {
            Id = Guid.NewGuid(),
            UserId = owner,
            LegalName = "Flota Test SRL",
            Cui = "RO123",
            RegisteredOffice = "București",
            LegalRepresentative = "Ion Ionescu",
            Slug = "flota-test",
        });
        await db.SaveChangesAsync();
        await Create(db, owner, NewRental(car.Id) with
        {
            PaymentDueDay = null,
            PaymentMethod = null,
            FranchiseBani = null,
            NoticeDays = null,
            UnavailabilityRule = null,
            HasKmLimit = true,
            MileageLimit = null,
            ExtraKmCostBani = 0,
            Tenant = new NewTenantInput("Chiriaș SRL", "Srl", null, null, null, "RO1", null, "Cluj", null, null, null),
        });

        Rental rental = await db.Rentals.Include(r => r.Tenant).SingleAsync();
        CompanyProfile company = await db.CompanyProfiles.SingleAsync();
        IReadOnlyList<MissingField> before = RentalDocumentRequirements.For(RentalDocumentType.RentalContract, rental, car, company, rental.Tenant);
        before.Count.ShouldBe(13);

        Result result = await new FillDocumentFieldsCommandHandler(db, new User(owner)).Handle(
            new FillDocumentFieldsCommand(
                rental.Id,
                CompanyRegCom: "J40/123/2024",
                CompanyIban: "ro49 aaaa 1b31 0075 9384 0000",
                CompanyLegalRepresentativeRole: "administrator",
                CarYear: 2021,
                TenantRegCom: "J12/1/2020",
                TenantRepresentativeName: "Maria Ionescu",
                RentalPaymentDueDay: "Monday",
                RentalPaymentMethod: "Card",
                RentalNoticeDays: 10,
                RentalFranchiseBani: 0,
                RentalUnavailabilityRule: "NotDue",
                RentalMileageLimit: 1_500,
                RentalExtraKmCostBani: 40),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Description : null);
        company.Iban.ShouldBe("RO49AAAA1B31007593840000");
        RentalDocumentRequirements.For(RentalDocumentType.RentalContract, rental, car, company, rental.Tenant).ShouldBeEmpty();
    }

    [Fact]
    public async Task Fleet_defaults_round_trip_with_canonical_names()
    {
        (ApplicationDbContext db, Guid owner, _) = await Seed();

        Result<RentalDefaultsDto> saved = await new SaveRentalDefaultsCommandHandler(db, new User(owner)).Handle(
            new SaveRentalDefaultsCommand(new RentalDefaultsDto(180_000, 100_000, 7, false, null, null, null, null,
                PaymentDueDay: "monday", PaymentMethod: "banktransfer", LatePenaltyPercentPerDay: 0.5m,
                FranchiseBani: 150_000, NoticeDays: 15, UnavailabilityRule: "proportional")),
            CancellationToken.None);

        saved.IsSuccess.ShouldBeTrue();
        saved.Value.PaymentDueDay.ShouldBe("Monday");
        saved.Value.PaymentMethod.ShouldBe("BankTransfer");

        Result<RentalDefaultsDto> read = await new GetRentalDefaultsQueryHandler(db, new User(owner))
            .Handle(new GetRentalDefaultsQuery(), CancellationToken.None);

        read.Value.UnavailabilityRule.ShouldBe("Proportional");
        read.Value.FranchiseBani.ShouldBe(150_000);
        read.Value.NoticeDays.ShouldBe(15);
        read.Value.LatePenaltyPercentPerDay.ShouldBe(0.5m);
    }

    [Fact]
    public async Task Saving_a_check_in_without_the_new_fields_keeps_them()
    {
        (ApplicationDbContext db, Guid owner, Car car) = await Seed();
        await Create(db, owner, NewRental(car.Id));
        Rental rental = await db.Rentals.SingleAsync();
        var handler = new SaveCheckRecordCommandHandler(db, new User(owner));

        await handler.Handle(
            new SaveCheckRecordCommand(rental.Id, CheckKind.CheckIn, DateTime.UtcNow, 45_000, "Plin", ["Chei"], null, null, null, null, null, null,
                KeysCount: 2, Documents: [HandoverDocumentCodes.Rca], Damages: "Zgârietură"),
            CancellationToken.None);

        await handler.Handle(
            new SaveCheckRecordCommand(rental.Id, CheckKind.CheckIn, DateTime.UtcNow, 45_010, "Plin", ["Chei"], null, null, null, null, null, null),
            CancellationToken.None);

        CheckRecord record = await db.CheckRecords.SingleAsync();
        record.Mileage.ShouldBe(45_010);
        record.KeysCount.ShouldBe(2);
        record.Documents.ShouldBe([HandoverDocumentCodes.Rca]);
        record.Damages.ShouldBe("Zgârietură");
    }

    [Fact]
    public async Task The_company_profile_form_keeps_the_representative_role_when_it_does_not_send_it()
    {
        (ApplicationDbContext db, Guid owner, _) = await Seed();
        db.CompanyProfiles.Add(new CompanyProfile
        {
            Id = Guid.NewGuid(),
            UserId = owner,
            LegalName = "Flota Test SRL",
            Slug = "flota-test",
            LegalRepresentativeRole = "administrator",
        });
        await db.SaveChangesAsync();
        var handler = new UpdateCompanyProfileCommandHandler(db, new User(owner));

        await handler.Handle(
            new UpdateCompanyProfileCommand("Flota Test SRL", "RO1", null, "Ion", "București", null, null, null, null, true, true, true, true),
            CancellationToken.None);
        (await db.CompanyProfiles.SingleAsync()).LegalRepresentativeRole.ShouldBe("administrator");

        Result<Application.Companies.CompanyProfileDto> updated = await handler.Handle(
            new UpdateCompanyProfileCommand("Flota Test SRL", "RO1", null, "Ion", "București", null, null, null, null, true, true, true, true, "asociat unic"),
            CancellationToken.None);
        updated.Value.LegalRepresentativeRole.ShouldBe("asociat unic");
    }

    private static CreateRentalCommand NewRental(Guid carId) => new(
        carId,
        null,
        new NewTenantInput("Adrian Popescu", "Individual", "1900101123456", "CJ", "123456", null, null, "Cluj-Napoca", null, null, null),
        new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc),
        45_000,
        100_000,
        0,
        false,
        null,
        0,
        null,
        "Plin",
        45_000,
        ["Chei"],
        null,
        null,
        PaymentDueDay: "Monday",
        PaymentMethod: "BankTransfer",
        FranchiseBani: 150_000,
        NoticeDays: 15,
        UnavailabilityRule: "NotDue",
        AdditionalDriverName: "Mihai Pop",
        AdditionalDriverCnp: "1850202123456",
        AdditionalDriverLicense: "CJ123456",
        KeysCount: 2,
        HandoverDocuments: [HandoverDocumentCodes.RegistrationCertificate, HandoverDocumentCodes.Rca, " "]);

    private static Task<Result<Guid>> Create(ApplicationDbContext db, Guid owner, CreateRentalCommand command) =>
        new CreateRentalCommandHandler(db, new User(owner), new Codes()).Handle(command, CancellationToken.None);

    private static async Task<(ApplicationDbContext Db, Guid Owner, Car Car)> Seed()
    {
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new Events());

        var owner = new Domain.Users.User { Id = Guid.NewGuid(), Role = UserRole.CarPoster, Email = "fleet@example.test" };
        var car = new Car
        {
            Id = Guid.NewGuid(),
            Brand = "Dacia",
            Model = "Logan",
            Year = 2022,
            Slug = "dacia-logan",
            Engine = "Benzină",
            Transmission = "Manuală",
            Location = "București",
            PlateNumber = "B 123 RID",
            Vin = "UU1TEST0000000001",
            PostedByUserId = owner.Id,
        };

        db.Users.Add(owner);
        db.Cars.Add(car);
        await db.SaveChangesAsync();
        return (db, owner.Id, car);
    }

    private sealed record User(Guid UserId) : IUserContext;

    private sealed class Codes : IRentalCodeGenerator
    {
        private int _next;

        public Task<string> NextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult($"RL-{Interlocked.Increment(ref _next):D6}");
    }

    private sealed class Events : Infrastructure.DomainEvents.IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
