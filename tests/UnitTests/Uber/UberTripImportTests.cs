using Application.Abstractions.Authentication;
using Application.PfaDashboard;
using Application.Uber;
using Domain.PfaRegistrations;
using Domain.Uber;
using Domain.Users;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Uber;

/// <summary>
/// Cursele din raportul Uber ajung în istoricul curselor, lângă cele Bolt. CSV-ul are forma
/// raportului real („Istoric curse”), cu date inventate.
/// </summary>
public sealed class UberTripImportTests
{
    private const string TripsHeader =
        "﻿Identificatorul universal unic (UUID) al cursei,Identificatorul universal unic (UUID) al șoferului,"
        + "Prenumele șoferului,Numele de familie al șoferului,Identificatorul universal unic (UUID) al vehiculului,"
        + "Numărul de înmatriculare,Tipul serviciului,Ora la care a fost comandată cursa,Ora sosirii la destinație,"
        + "Adresa de preluare,Adresa destinației,Distanța cursei,Starea cursei,Tipul produsului,Tip de plată";

    private const string Driver = "11111111-1111-1111-1111-111111111111,Test,Sofer,22222222-2222-2222-2222-222222222222,B00TST,personal_transport";

    private static readonly string TripsCsv = string.Join("\r\n",
        TripsHeader,
        $"aaaaaaaa-0000-0000-0000-000000000001,{Driver},6/22/2026 17:32,6/22/2026 18:00,\"Strada Test 1, București, România\",\"Bulevardul Exemplu 2, București, România\",6.29,completed,Comfort,apple_pay",
        $"aaaaaaaa-0000-0000-0000-000000000002,{Driver},6/22/2026 15:57,6/22/2026 16:28,\"Șoseaua Test 3, București\",\"Piața Exemplu 4, Voluntari\",13.94,completed,Black,cash",
        $"aaaaaaaa-0000-0000-0000-000000000003,{Driver},6/22/2026 14:50,,\"Strada Anulată 5, București\",\"Strada Anulată 6, București\",0,rider_cancelled,Comfort,braintree");

    [Fact]
    public async Task ImportedTripsAppearInTheRideHistory()
    {
        await using ApplicationDbContext db = Database();
        PfaRegistration pfa = await AddPfa(db);

        Result<UberDashboardResponse> imported = await UberCsvImporter.ImportAsync(
            db, pfa, [new UberCsvUpload("curse.csv", TripsCsv)], null, null, pfa.UserId, default);
        imported.IsSuccess.ShouldBeTrue();

        (await db.UberTrips.CountAsync()).ShouldBe(3);
        UberCsvImport import = await db.UberCsvImports.SingleAsync();
        (import.Year, import.Month, import.Trips).ShouldBe((2026, 6, 2));

        PfaRidesPageResponse page = await Rides(db, pfa.UserId, platform: null);
        page.UberRidesAvailable.ShouldBeTrue();
        page.Total.ShouldBe(2); // anulata nu e cursă făcută

        PfaRideResponse first = page.Items[0];
        first.Platform.ShouldBe("uber");
        // 17:32 ora României, vara (UTC+3).
        first.StartedAtUtc.ShouldBe(new DateTime(2026, 6, 22, 14, 32, 0, DateTimeKind.Utc));
        first.DurationMin.ShouldBe(28);
        first.DistanceKm.ShouldBe(6.3);
        first.Category.ShouldBe("Comfort");
        first.PaymentType.ShouldBe("card");
        first.Net.ShouldBeNull();

        (await Rides(db, pfa.UserId, platform: "bolt")).Total.ShouldBe(0);
        (await Rides(db, pfa.UserId, platform: "uber", payment: "cash")).Items.Single().Pickup.ShouldBe("Șoseaua Test 3, București");
    }

    [Fact]
    public async Task ReuploadingAnOlderImportFillsInItsTripsOnce()
    {
        await using ApplicationDbContext db = Database();
        PfaRegistration pfa = await AddPfa(db);

        // Un import de dinainte: totalurile există, cursele nu.
        db.UberCsvImports.Add(new UberCsvImport
        {
            Id = Guid.NewGuid(), UserId = pfa.UserId, PfaRegistrationId = pfa.Id, Year = 2026, Month = 6,
            FileType = "trips", FileName = "curse.csv", Trips = 2, Kilometers = 20.23,
        });
        await db.SaveChangesAsync();

        (await UberCsvImporter.ImportAsync(db, pfa, [new UberCsvUpload("curse.csv", TripsCsv)], null, null, pfa.UserId, default))
            .IsSuccess.ShouldBeTrue();
        (await db.UberTrips.CountAsync()).ShouldBe(3);
        (await db.UberCsvImports.CountAsync()).ShouldBe(1);

        Result<UberDashboardResponse> again = await UberCsvImporter.ImportAsync(
            db, pfa, [new UberCsvUpload("curse.csv", TripsCsv)], null, null, pfa.UserId, default);
        again.Error.Code.ShouldBe("Uber.DuplicateImport");
        (await db.UberTrips.CountAsync()).ShouldBe(3);
    }

    private static async Task<PfaRidesPageResponse> Rides(ApplicationDbContext db, Guid userId, string? platform, string? payment = null)
    {
        var handler = new GetPfaDashboardRidesQueryHandler(db, new CurrentUser(userId));
        Result<PfaRidesPageResponse> result = await handler.Handle(
            new GetPfaDashboardRidesQuery(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), platform, payment, 1, 50, "-date", null),
            default);
        return result.Value;
    }

    private static async Task<PfaRegistration> AddPfa(ApplicationDbContext db)
    {
        var user = new User { Id = Guid.NewGuid(), Email = "sofer@example.test", FirstName = "Test", LastName = "Sofer" };
        var pfa = new PfaRegistration { Id = Guid.NewGuid(), UserId = user.Id, User = user, FullName = "Test Sofer" };
        db.Users.Add(user);
        db.PfaRegistrations.Add(pfa);
        await db.SaveChangesAsync();
        return pfa;
    }

    private static ApplicationDbContext Database() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Events());

    private sealed record CurrentUser(Guid UserId) : IUserContext;

    private sealed class Events : Infrastructure.DomainEvents.IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
