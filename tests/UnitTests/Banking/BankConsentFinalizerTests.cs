using Application.Abstractions.Security;
using Application.Abstractions.Services;
using Application.Banking;
using Domain.Banking;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using SharedKernel;
using Xunit;

namespace UnitTests.Banking;

/// <summary>
/// Drumul de la „l-am trimis la bancă" la „avem conturile".
///
/// Cazul care contează cel mai mult e cel mai puțin evident: cât timp omul n-a semnat la bancă,
/// furnizorul răspunde 401 la orice întrebare despre consimțământ — nu „în așteptare", ci refuz.
/// Citit greșit, ar omorî conexiunea la două secunde după ce l-am trimis acolo.
/// </summary>
public sealed class BankConsentFinalizerTests
{
    [Fact]
    public async Task RefusalBeforeTheUserSigns_KeepsTheConnectionWaiting()
    {
        await using ApplicationDbContext db = Database();
        BankConnection connection = await AddConnection(db);

        var provider = new StubProvider
        {
            OnGetConsent = () => throw new BankDataConsentExpiredException("UNAUTHORIZED"),
        };

        BankConnectionStatus status = await Finalizer(db, provider).TryFinalizeAsync(connection, default);

        status.ShouldBe(BankConnectionStatus.Created);
        connection.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task RefusalAfterTheAuthorisationWindowClosed_EndsTheAttempt()
    {
        // Singurul lucru care încheie o conectare neterminată e termenul adresei băncii. Altfel un
        // om care lasă tabul deschis un sfert de oră, cât își caută parola, ar găsi totul abandonat.
        await using ApplicationDbContext db = Database();
        BankConnection connection = await AddConnection(db, linkExpiresAtUtc: DateTime.UtcNow.AddMinutes(-1));

        var provider = new StubProvider
        {
            OnGetConsent = () => throw new BankDataConsentExpiredException("UNAUTHORIZED"),
        };

        BankConnectionStatus status = await Finalizer(db, provider).TryFinalizeAsync(connection, default);

        status.ShouldBe(BankConnectionStatus.Error);
        connection.ErrorMessage.ShouldNotBeNull();
    }

    [Fact]
    public async Task ConsentNotYetAuthorised_KeepsWaitingAndRemembersTheStatus()
    {
        await using ApplicationDbContext db = Database();
        BankConnection connection = await AddConnection(db);

        // Furnizorul întoarce stările cu majuscule („RECEIVED"), nu cum le scrie standardul.
        var provider = new StubProvider { Consent = new BankConsentState("RECEIVED", null) };

        BankConnectionStatus status = await Finalizer(db, provider).TryFinalizeAsync(connection, default);

        status.ShouldBe(BankConnectionStatus.Created);
        connection.ConsentStatus.ShouldBe("RECEIVED");
    }

    [Fact]
    public async Task AuthorisedConsent_LinksTheConnectionAndItsAccounts()
    {
        await using ApplicationDbContext db = Database();
        BankConnection connection = await AddConnection(db);

        DateTime validUntil = DateTime.UtcNow.AddDays(180);
        var provider = new StubProvider
        {
            // Majuscule și aici: potrivirea trebuie să fie insensibilă la registru.
            Consent = new BankConsentState("VALID", validUntil),
            Accounts =
            [
                new BankAccountDetailsInfo("acc-1", "RO49AAAA1B31007593840000", "RON", "Ion Pop"),
            ],
        };

        BankConnectionStatus status = await Finalizer(db, provider).TryFinalizeAsync(connection, default);

        status.ShouldBe(BankConnectionStatus.Linked);
        connection.ConsentExpiresAtUtc.ShouldBe(validUntil);

        BankAccount account = await db.BankAccounts.SingleAsync(a => a.BankConnectionId == connection.Id);
        account.ProviderAccountId.ShouldBe("acc-1");
        // IBAN-ul complet nu se stochează niciodată.
        account.IbanMasked.ShouldBe("RO49••••0000");
        account.OwnerName.ShouldBe("Ion Pop");
    }

    [Fact]
    public async Task ConnectionFromAPreviousProvider_IsRetiredInsteadOfProbed()
    {
        await using ApplicationDbContext db = Database();
        BankConnection connection = await AddConnection(db);
        connection.Provider = "Fintable";
        await db.SaveChangesAsync();

        var provider = new StubProvider { OnGetConsent = () => throw new InvalidOperationException("nu se atinge") };

        BankConnectionStatus status = await Finalizer(db, provider).TryFinalizeAsync(connection, default);

        status.ShouldBe(BankConnectionStatus.Revoked);
    }

    private static BankConsentFinalizer Finalizer(ApplicationDbContext db, IBankDataProvider provider) =>
        new(db, provider, new PassThroughProtector(), new BankAccountSyncService(db, provider));

    private static ApplicationDbContext Database() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private static async Task<BankConnection> AddConnection(
        ApplicationDbContext db,
        DateTime? linkExpiresAtUtc = null)
    {
        var user = new User { Id = Guid.NewGuid(), Email = "sofer@example.test" };
        db.Users.Add(user);

        var connection = new BankConnection
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Provider = "SmartAccounts",
            InstitutionId = "BT",
            InstitutionName = "Banca Transilvania",
            ProviderConsentId = "243990",
            Reference = Guid.NewGuid().ToString("N"),
            Status = BankConnectionStatus.Created,
            LinkExpiresAtUtc = linkExpiresAtUtc ?? DateTime.UtcNow.AddMinutes(30),
            CreatedAtUtc = DateTime.UtcNow,
            MaxHistoricalDays = 120,
        };

        db.BankConnections.Add(connection);
        await db.SaveChangesAsync();
        return connection;
    }

    private sealed class StubProvider : IBankDataProvider
    {
        public Func<BankConsentState>? OnGetConsent { get; init; }

        public BankConsentState Consent { get; init; } = new(null, null);

        public IReadOnlyList<BankAccountDetailsInfo> Accounts { get; init; } = [];

        public string ProviderName => "SmartAccounts";

        public bool IsConfigured => true;

        public Task<BankConsentState> GetConsentAsync(string bankCode, string consentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OnGetConsent is null ? Consent : OnGetConsent());

        public Task<IReadOnlyList<BankAccountDetailsInfo>> ListAccountsAsync(
            string bankCode, string consentId, CancellationToken cancellationToken = default) => Task.FromResult(Accounts);

        public Task<BankTransactionsPage> GetTransactionsAsync(
            string bankCode, string consentId, string resourceId, DateOnly? dateFrom, DateOnly? dateTo, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BankTransactionsPage([], []));

        public Task<IReadOnlyList<BankInstitutionInfo>> ListInstitutionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BankInstitutionInfo>>([]);

        public Task<BankConsentCreated> CreateConsentAsync(BankConsentRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteConsentAsync(string bankCode, string consentId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>Testele nu verifică cifrarea; verifică ce se scrie și când.</summary>
    private sealed class PassThroughProtector : ISecretProtector
    {
        public string Protect(string plainText) => plainText;

        public string Unprotect(string protectedText) => protectedText;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
