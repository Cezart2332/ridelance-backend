using Application.Abstractions.Authentication;
using Application.Banking.Queries;
using Domain.Banking;
using Domain.PfaRegistrations;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using SharedKernel;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace UnitTests.Banking;

/// <summary>
/// Cine vede mișcările din cont și cum se grupează.
///
/// Datele bancare sunt cele mai sensibile din platformă, iar de acum le citește și contabilul.
/// Testele de acces sunt aici pentru că regula e una singură (<c>AssignedContabilId</c>) și
/// trebuie să rămână una singură: un contabil vede clienții lui și numai pe ei.
/// </summary>
public sealed class BankActivityTests
{
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly Guid Accountant = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    [Fact]
    public async Task The_client_sees_their_own_movements()
    {
        using ApplicationDbContext db = await Seeded();

        BankActivityResponse activity = await Activity(db, Client, target: null);

        activity.TotalIn.ShouldBe(1000m);
        activity.TotalOut.ShouldBe(250m);
        // Patru mișcări ale lui, pe tot anul. A cincea e a altcuiva și nu se numără.
        activity.TransactionCount.ShouldBe(4);
    }

    [Fact]
    public async Task The_assigned_accountant_sees_the_client()
    {
        using ApplicationDbContext db = await Seeded();

        BankActivityResponse activity = await Activity(db, Accountant, target: Client);

        activity.TotalIn.ShouldBe(1000m);
    }

    /// <summary>
    /// Un contabil care nu e al clientului nu vede nimic — nici măcar totalurile.
    ///
    /// Fără regula asta, orice cont de contabil ar fi putut citi conturile oricui doar punând alt
    /// id în adresă: interogarea lucrează pe id-ul primit, nu pe cel din sesiune.
    /// </summary>
    [Fact]
    public async Task An_accountant_of_someone_else_is_refused()
    {
        using ApplicationDbContext db = await Seeded();

        Result<BankActivityResponse> result = await Handle(db, Stranger, Client);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Bank.NotAllowed");
    }

    /// <summary>Pe un an se cer coloane lunare, nu 365 de zile.</summary>
    [Fact]
    public async Task A_year_is_grouped_by_month()
    {
        using ApplicationDbContext db = await Seeded();

        BankActivityResponse activity = await Activity(
            db,
            Client,
            target: null,
            from: new DateOnly(2026, 1, 1),
            to: new DateOnly(2026, 12, 31),
            bucket: "month");

        activity.Buckets.Count.ShouldBe(2);
        activity.Buckets[0].Start.ShouldBe(new DateOnly(2026, 3, 1));
        activity.Buckets[0].In.ShouldBe(1000m);
        activity.Buckets[1].Start.ShouldBe(new DateOnly(2026, 4, 1));
    }

    /// <summary>Săptămâna începe luni, nu duminică.</summary>
    [Fact]
    public async Task A_week_starts_on_monday()
    {
        using ApplicationDbContext db = await Seeded();

        BankActivityResponse activity = await Activity(
            db,
            Client,
            target: null,
            from: new DateOnly(2026, 3, 1),
            to: new DateOnly(2026, 3, 31),
            bucket: "week");

        // 10 și 12 martie 2026 cad în aceeași săptămână, care începe luni, pe 9.
        activity.Buckets[0].Start.ShouldBe(new DateOnly(2026, 3, 9));
        activity.Buckets[0].Count.ShouldBe(2);
    }

    [Fact]
    public async Task Transactions_outside_the_period_are_left_out()
    {
        using ApplicationDbContext db = await Seeded();

        BankActivityResponse activity = await Activity(
            db,
            Client,
            target: null,
            from: new DateOnly(2026, 4, 1),
            to: new DateOnly(2026, 4, 30));

        activity.TransactionCount.ShouldBe(1);
        activity.TotalIn.ShouldBe(0m);
        activity.TotalOut.ShouldBe(50m);
    }

    [Fact]
    public async Task ReconnectingAnotherBankAccount_HidesTransactionsFromThePreviousConsent()
    {
        using ApplicationDbContext db = await Seeded();
        BankConnection connection = await db.BankConnections.SingleAsync();
        BankAccount account = await db.BankAccounts.SingleAsync();
        connection.ProviderConsentId = "consent-next";
        db.BankTransactions.Add(Transaction(account.Id, new DateOnly(2026, 9, 29), 25m, consentId: "consent-next"));
        await db.SaveChangesAsync();

        BankActivityResponse activity = await Activity(db, Client, null);
        BankTransactionsResponse list = (await new GetBankTransactionsQueryHandler(db, new StubUser(Client))
            .Handle(new GetBankTransactionsQuery(null, null, 1, 25), CancellationToken.None)).Value;

        activity.TransactionCount.ShouldBe(1);
        activity.TotalIn.ShouldBe(25m);
        list.TotalCount.ShouldBe(1);
        list.Items.Single().Amount.ShouldBe(25m);
    }

    [Fact]
    public async Task DisconnectingTheBank_HidesItsHistoricalTransactions()
    {
        using ApplicationDbContext db = await Seeded();
        (await db.BankConnections.SingleAsync()).Status = BankConnectionStatus.Revoked;
        await db.SaveChangesAsync();

        (await Activity(db, Client, null)).TransactionCount.ShouldBe(0);
        (await new GetBankTransactionsQueryHandler(db, new StubUser(Client))
            .Handle(new GetBankTransactionsQuery(null, null, 1, 25), CancellationToken.None)).Value.TotalCount.ShouldBe(0);
    }

    private static async Task<BankActivityResponse> Activity(
        ApplicationDbContext db,
        Guid requester,
        Guid? target,
        DateOnly? from = null,
        DateOnly? to = null,
        string bucket = "day")
    {
        Result<BankActivityResponse> result =
            await Handle(db, requester, target, from, to, bucket);

        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private static Task<Result<BankActivityResponse>> Handle(
        ApplicationDbContext db,
        Guid requester,
        Guid? target,
        DateOnly? from = null,
        DateOnly? to = null,
        string bucket = "day")
    {
        var handler = new GetBankActivityQueryHandler(db, new StubUser(requester));

        return handler.Handle(
            new GetBankActivityQuery(
                from ?? new DateOnly(2026, 1, 1),
                to ?? new DateOnly(2026, 12, 31),
                bucket,
                target),
            CancellationToken.None);
    }

    private static async Task<ApplicationDbContext> Seeded()
    {
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new Events());

        db.PfaRegistrations.Add(new PfaRegistration
        {
            Id = Guid.NewGuid(),
            UserId = Client,
            AssignedContabilId = Accountant,
        });

        var connection = new BankConnection
        {
            Id = Guid.NewGuid(),
            UserId = Client,
            Provider = "SmartAccounts",
            ProviderConsentId = "consent-current",
            Status = BankConnectionStatus.Linked,
            LinkedAtUtc = DateTime.UtcNow,
        };
        db.BankConnections.Add(connection);

        var account = new BankAccount
        {
            Id = Guid.NewGuid(),
            BankConnectionId = connection.Id,
            UserId = Client,
            ProviderAccountId = "acc-1",
            Iban = "RO49AAAA1B31007593841234",
            Currency = "RON",
            IsActive = true,
        };
        db.BankAccounts.Add(account);

        db.BankTransactions.AddRange(
            Transaction(account.Id, new DateOnly(2026, 3, 10), 600m),
            Transaction(account.Id, new DateOnly(2026, 3, 12), 400m),
            Transaction(account.Id, new DateOnly(2026, 3, 20), -200m),
            Transaction(account.Id, new DateOnly(2026, 4, 5), -50m),
            // Al altui client: nu are ce căuta în totaluri.
            Transaction(account.Id, new DateOnly(2026, 3, 15), 9999m, Stranger),
            // Același RIDElance și același rând de cont, dar alt acord bancar.
            Transaction(account.Id, new DateOnly(2026, 3, 16), 9999m, consentId: "consent-old"));

        await db.SaveChangesAsync(CancellationToken.None);
        return db;
    }

    private static BankTransaction Transaction(
        Guid accountId, DateOnly date, decimal amount, Guid? userId = null, string consentId = "consent-current") =>
        new()
        {
            Id = Guid.NewGuid(),
            BankAccountId = accountId,
            UserId = userId ?? Client,
            ProviderConsentId = consentId,
            ProviderTransactionId = Guid.NewGuid().ToString(),
            BookingDate = date,
            Amount = amount,
            Currency = "RON",
            ImportedAtUtc = DateTime.UtcNow,
        };

    private sealed class StubUser(Guid id) : IUserContext
    {
        public Guid UserId { get; } = id;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
