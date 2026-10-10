using Application.Abstractions.Authentication;
using Application.Abstractions.Services;
using Application.Mailboxes;
using Domain.Documents;
using Domain.Mailboxes;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.ArrFleet;
using Domain.Users;
using Infrastructure.Accounting;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Infrastructure.Mailboxes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using UnitTests.Accounting;
using Xunit;

namespace UnitTests.Mailboxes;

/// <summary>Spec faza 2: emailul operațional per client, pe furnizorul în memorie.</summary>
public sealed class ClientMailboxTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly IOptions<MailboxOptions> _options = Options.Create(new MailboxOptions { CredentialsKey = "test-key" });
    private readonly FakeMailboxProvider _fake;
    private readonly MailboxCredentialProtector _secrets;
    private readonly MemoryFiles _files = new();
    private readonly Guid _admin = Guid.NewGuid();

    public ClientMailboxTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        _fake = new FakeMailboxProvider(_options);
        _secrets = new MailboxCredentialProtector(_options);
        _db.Users.Add(new User { Id = _admin, Email = "admin@ridelance.ro", FirstName = "Admin", LastName = "RIDElance", Role = UserRole.Admin });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    // ---------- §4 Generarea adresei ----------

    [Theory]
    [InlineData("Ion", "Popescu", "ion.popescu")]
    // Exemplul din spec: primul prenume, diacritice cu virgulă.
    [InlineData("Ștefan-Andrei", "Țăranu", "stefan.taranu")]
    // Aceleași litere, cu sedilă (tastatura veche).
    [InlineData("Ştefan", "Ţăranu", "stefan.taranu")]
    [InlineData("Ana Maria", "Înălțeanu", "ana.inalteanu")]
    // Numele de familie compus rămâne întreg, legat cu cratimă.
    [InlineData("Maria", "Popa-Ionescu", "maria.popa-ionescu")]
    [InlineData("Maria", "Popa  Ionescu", "maria.popa-ionescu")]
    [InlineData("  Dănuț ", "O'Neill", "danut.oneill")]
    public void Address_ComesFromTheFirstGivenNameAndTheFamilyName(string first, string last, string expected) =>
        MailboxAddress.BaseLocalPart(first, last).ShouldBe(expected);

    [Fact]
    public void Address_NeedsBothNames_AndNumbersCollisions()
    {
        MailboxAddress.BaseLocalPart("Ion", " ").ShouldBeNull();
        MailboxAddress.BaseLocalPart("李", "Popescu").ShouldBeNull();

        MailboxAddress.WithSuffix("ion.popescu", 1).ShouldBe("ion.popescu");
        MailboxAddress.WithSuffix("ion.popescu", 2).ShouldBe("ion.popescu2");
        MailboxAddress.OpsIdentityOf("stefan.taranu").ShouldBe("rid-ops-stefan.taranu");
    }

    // ---------- §5 Fluxul de creare ----------

    [Fact]
    public async Task ClientAtArrStep_GetsMailboxAndIdentity_WithoutAnyone()
    {
        Guid pfa = Client("Ștefan-Andrei", "Țăranu");

        await Process();

        ClientMailbox mailbox = await _db.ClientMailboxes.SingleAsync();
        mailbox.Status.ShouldBe(ClientMailboxStatus.Active);
        mailbox.PfaRegistrationId.ShouldBe(pfa);
        mailbox.Address.ShouldBe("stefan.taranu@pfa.ridelance.ro");
        mailbox.OpsIdentityAddress.ShouldBe("rid-ops-stefan.taranu@pfa.ridelance.ro");
        mailbox.ActivatedAtUtc.ShouldNotBeNull();

        FakeMailbox created = _fake.Mailboxes["stefan.taranu"];
        created.Identities.Keys.ShouldBe(["rid-ops-stefan.taranu"]);

        // Parolele: generate, lungi, diferite între ele și niciodată în clar în baza de date.
        string identityPassword = created.Identities["rid-ops-stefan.taranu"];
        created.Password.Length.ShouldBeGreaterThanOrEqualTo(24);
        identityPassword.Length.ShouldBeGreaterThanOrEqualTo(24);
        identityPassword.ShouldNotBe(created.Password);
        mailbox.MailboxPasswordEncrypted.ShouldNotBe(created.Password);
        _secrets.Unprotect(mailbox.MailboxPasswordEncrypted!).ShouldBe(created.Password);
        _secrets.Unprotect(mailbox.OpsIdentityPasswordEncrypted!).ShouldBe(identityPassword);

        // A doua trecere a jobului nu mai face nimic.
        await Process();
        _fake.Mailboxes.Count.ShouldBe(1);
        (await _db.ClientMailboxes.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task OnlyClientsCurrentlyAtTheStep_AreQueuedAutomatically()
    {
        Client("Ion", "Popescu", SignaturePacketStatus.Sent);
        Client("Ana", "Ionescu", SignaturePacketStatus.Completed, ArrFleetStatus.Completed);

        await Process();

        (await _db.ClientMailboxes.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Collisions_GetANumber_LocallyAndAtTheProvider()
    {
        Client("Ion", "Popescu");
        Client("Ion", "Popescu");
        // Al treilea omonim există doar la furnizor (creat de mână în Migadu).
        await _fake.CreateMailboxAsync(new CreateMailboxRequest("Altcineva", "ion.popescu3", "x"), default);
        Client("Ion", "Popescu");

        await Process();

        (await _db.ClientMailboxes.Select(m => m.LocalPart).ToListAsync())
            .ShouldBe(["ion.popescu", "ion.popescu2", "ion.popescu4"], ignoreOrder: true);
    }

    [Fact]
    public async Task RetryAfterAPartialFailure_NeverCreatesASecondMailbox()
    {
        Guid pfa = Client("Ion", "Popescu");
        var flaky = new FailingIdentities(_fake) { Failures = 1 };

        await Process(flaky);

        // Mailbox-ul există, identitatea nu: eșuat, cu eroarea salvată și adminii anunțați.
        ClientMailbox mailbox = await _db.ClientMailboxes.SingleAsync();
        mailbox.Status.ShouldBe(ClientMailboxStatus.Failed);
        mailbox.LastError.ShouldNotBeNullOrEmpty();
        mailbox.MailboxCreatedAtUtc.ShouldNotBeNull();
        (await _db.Notifications.CountAsync(n => n.UserId == _admin)).ShouldBe(1);
        string password = _fake.Mailboxes["ion.popescu"].Password;

        // „Reîncearcă” din admin, apoi jobul.
        Result<ClientMailboxDto> retried = await new RequestClientMailboxCommandHandler(_db, new FixedUser(_admin))
            .Handle(new RequestClientMailboxCommand(pfa), default);
        retried.Value.Status.ShouldBe(nameof(ClientMailboxStatus.Creating));
        await Process(flaky);

        mailbox.Status.ShouldBe(ClientMailboxStatus.Active);
        mailbox.LastError.ShouldBeNull();
        _fake.Mailboxes.Keys.ShouldBe(["ion.popescu"]);
        _fake.Mailboxes["ion.popescu"].Password.ShouldBe(password);
        _fake.Mailboxes["ion.popescu"].Identities.Count.ShouldBe(1);
        (await Actions(mailbox)).ShouldBe(
            [ClientMailboxAuditAction.Created, ClientMailboxAuditAction.Failed, ClientMailboxAuditAction.Retried, ClientMailboxAuditAction.IdentityCreated]);
    }

    /// <summary>Mailbox-ul și identitatea au ajuns la furnizor, dar aplicația a căzut înainte să noteze.</summary>
    [Fact]
    public async Task ResumingAfterACrash_AdoptsWhatAlreadyExistsAtTheProvider()
    {
        Guid pfa = Client("Ion", "Popescu", SignaturePacketStatus.Sent);
        string known = MailboxPasswords.Generate();
        _db.ClientMailboxes.Add(new ClientMailbox
        {
            Id = Guid.NewGuid(), UserId = await _db.PfaRegistrations.Where(p => p.Id == pfa).Select(p => p.UserId).SingleAsync(), PfaRegistrationId = pfa,
            Status = ClientMailboxStatus.Creating, LocalPart = "ion.popescu", Address = "ion.popescu@pfa.ridelance.ro",
            MailboxPasswordEncrypted = _secrets.Protect(known),
            OpsIdentityLocalPart = "rid-ops-ion.popescu", OpsIdentityAddress = "rid-ops-ion.popescu@pfa.ridelance.ro",
            OpsIdentityPasswordEncrypted = _secrets.Protect("parola-identitatii-pe-care-o-stim"),
        });
        await _db.SaveChangesAsync();
        await _fake.CreateMailboxAsync(new CreateMailboxRequest("Ion Popescu", "ion.popescu", "parola-pierduta"), default);
        await _fake.CreateIdentityAsync("ion.popescu", new CreateIdentityRequest("RIDElance", "rid-ops-ion.popescu", "alta-parola-pierduta"), default);

        await Process();

        (await _db.ClientMailboxes.SingleAsync()).Status.ShouldBe(ClientMailboxStatus.Active);
        _fake.Mailboxes.Keys.ShouldBe(["ion.popescu"]);
        // Ce e la furnizor se potrivește cu ce avem noi salvat.
        _fake.Mailboxes["ion.popescu"].Password.ShouldBe(known);
        _fake.Mailboxes["ion.popescu"].Identities["rid-ops-ion.popescu"].ShouldBe("parola-identitatii-pe-care-o-stim");
    }

    /// <summary>Producție fără chei Migadu: nimic nu intră singur în coadă, deci niciun client nu produce un eșec.</summary>
    [Fact]
    public async Task UnconfiguredMigadu_QueuesNothingOnItsOwn()
    {
        Client("Ion", "Popescu");
        IOptions<MailboxOptions> unconfigured = Options.Create(new MailboxOptions { Provider = MailboxOptions.MigaduProvider });
        var provisioner = new ClientMailboxProvisioner(_db, _fake, _secrets, unconfigured);

        await new ProcessClientMailboxesCommandHandler(_db, provisioner, unconfigured).Handle(new ProcessClientMailboxesCommand(), default);

        (await _db.ClientMailboxes.AnyAsync()).ShouldBeFalse();
        unconfigured.Value.CanProvision.ShouldBeFalse();
        new MailboxOptions().CanProvision.ShouldBeTrue();
        new MailboxOptions { Provider = "Migadu", ApiUser = "a@b.ro", ApiKey = "k", CredentialsKey = "c" }.CanProvision.ShouldBeTrue();
    }

    // ---------- §6 Admin ----------

    [Fact]
    public async Task Admin_SeesNoPasswords_AndEveryRevealIsAudited()
    {
        Guid pfa = Client("Ion", "Popescu");

        ClientMailboxDto before = (await new GetClientMailboxQueryHandler(_db).Handle(new GetClientMailboxQuery(pfa), default)).Value;
        before.Status.ShouldBe(nameof(ClientMailboxStatus.NotCreated));

        await Process();
        ClientMailbox mailbox = await _db.ClientMailboxes.SingleAsync();

        ClientMailboxDto dto = (await new GetClientMailboxQueryHandler(_db).Handle(new GetClientMailboxQuery(pfa), default)).Value;
        (dto.Status, dto.Address, dto.OpsIdentityAddress).ShouldBe(("Active", "ion.popescu@pfa.ridelance.ro", "rid-ops-ion.popescu@pfa.ridelance.ro"));
        System.Text.Json.JsonSerializer.Serialize(dto).ShouldNotContain(_fake.Mailboxes["ion.popescu"].Password);

        var reveal = new RevealOpsCredentialsCommandHandler(_db, new FixedUser(_admin), _secrets);
        OpsCredentialsDto credentials = (await reveal.Handle(new RevealOpsCredentialsCommand(pfa), default)).Value;
        credentials.Address.ShouldBe("rid-ops-ion.popescu@pfa.ridelance.ro");
        credentials.Password.ShouldBe(_fake.Mailboxes["ion.popescu"].Identities["rid-ops-ion.popescu"]);
        await reveal.Handle(new RevealOpsCredentialsCommand(pfa), default);

        List<ClientMailboxAuditLog> reveals = await _db.ClientMailboxAuditLogs
            .Where(l => l.ClientMailboxId == mailbox.Id && l.Action == ClientMailboxAuditAction.CredentialsRevealed)
            .ToListAsync();
        reveals.Count.ShouldBe(2);
        reveals.ShouldAllBe(l => l.PerformedByUserId == _admin && l.Details == null);

        // Există deja: „Creează” nu face un al doilea.
        Result<ClientMailboxDto> again = await new RequestClientMailboxCommandHandler(_db, new FixedUser(_admin)).Handle(new RequestClientMailboxCommand(pfa), default);
        again.Error.ShouldBe(MailboxErrors.AlreadyExists);
    }

    // ---------- §7 Offboarding ----------

    [Fact]
    public async Task Transfer_RotatesThePassword_RemovesRidelance_AndKeepsTheMailbox()
    {
        Guid pfa = Client("Ion", "Popescu");
        await Process();
        ClientMailbox mailbox = await _db.ClientMailboxes.SingleAsync();
        string oldPassword = _fake.Mailboxes["ion.popescu"].Password;

        Result<ClientMailboxDto> transferred = await Transfer().Handle(new TransferClientMailboxCommand(pfa), default);

        transferred.IsSuccess.ShouldBeTrue();
        (transferred.Value.Status, transferred.Value.OpsIdentityAddress).ShouldBe(("Transferred", null));

        FakeMailbox remote = _fake.Mailboxes["ion.popescu"];
        remote.Password.ShouldNotBe(oldPassword);
        remote.RecoveryEmail.ShouldBe("client1@example.ro");
        remote.Identities.ShouldBeEmpty();

        // După predare, parolele nu mai sunt la noi.
        mailbox.MailboxPasswordEncrypted.ShouldBeNull();
        mailbox.OpsIdentityPasswordEncrypted.ShouldBeNull();
        mailbox.TransferredAtUtc.ShouldNotBeNull();
        (await new RevealOpsCredentialsCommandHandler(_db, new FixedUser(_admin), _secrets).Handle(new RevealOpsCredentialsCommand(pfa), default))
            .Error.ShouldBe(MailboxErrors.Transferred);

        // Documentul „Predare email”: generat de noi (nu apare în lista clientului), cu parola nouă.
        Document document = await _db.Documents.SingleAsync(d => d.Id == mailbox.HandoverDocumentId);
        (document.OriginalFileName, document.Origin, document.ContentType).ShouldBe(("Predare_email.pdf", DocumentOrigin.SystemGenerated, "application/pdf"));
        _files.Files[document.EncryptedFilePath].Length.ShouldBeGreaterThan(0);
        MailboxHandoverDocument.Build("ion.popescu@pfa.ridelance.ro", remote.Password, "Ion Popescu", "client1@example.ro", new DateOnly(2026, 10, 10))
            .Lines.Select(l => l.Cells[1]).ShouldContain(remote.Password);

        // Clientul își vede în continuare adresa.
        (await new GetOwnMailboxQueryHandler(_db, new FixedUser(mailbox.UserId)).Handle(new GetOwnMailboxQuery(), default))
            .Value!.Address.ShouldBe("ion.popescu@pfa.ridelance.ro");

        // A doua predare nu mai are ce preda.
        (await Transfer().Handle(new TransferClientMailboxCommand(pfa), default)).Error.ShouldBe(MailboxErrors.Transferred);
    }

    [Fact]
    public async Task ClientWithoutAnActiveMailbox_SeesNothing()
    {
        Client("Ion", "Popescu");
        Guid userId = await _db.PfaRegistrations.Select(p => p.UserId).SingleAsync();

        (await new GetOwnMailboxQueryHandler(_db, new FixedUser(userId)).Handle(new GetOwnMailboxQuery(), default)).Value.ShouldBeNull();
    }

    // ---------- §9 Limite ----------

    [Theory]
    [InlineData(159, 15, false)]
    [InlineData(160, 0, true)]
    [InlineData(0, 16, true)]
    public async Task Usage_AlertsFromEightyPercentOfEitherLimit(int incoming, int outgoing, bool alert)
    {
        var provider = new FixedUsage(new MailboxUsage(incoming, outgoing, 0.5m));

        MailboxUsageDto usage = (await new CheckMailboxUsageCommandHandler(_db, provider, _options).Handle(new CheckMailboxUsageCommand(), default)).Value;
        // A doua citire din aceeași zi nu mai trimite încă o alertă.
        await new CheckMailboxUsageCommandHandler(_db, provider, _options).Handle(new CheckMailboxUsageCommand(), default);

        (usage.IncomingLimit, usage.OutgoingLimit, usage.Alert).ShouldBe((200, 20, alert));
        (await _db.Notifications.CountAsync(n => n.UserId == _admin && n.Type == NotificationTypes.OnboardingStepAwaitingAdmin)).ShouldBe(alert ? 1 : 0);
    }

    // ---------- §3 Criptarea ----------

    [Fact]
    public void Credentials_AreEncryptedWithTheirOwnKey_AndMigaduRefusesToRunWithoutIt()
    {
        string secret = MailboxPasswords.Generate();
        string protectedOnce = _secrets.Protect(secret);

        protectedOnce.ShouldNotContain(secret);
        _secrets.Protect(secret).ShouldNotBe(protectedOnce);
        _secrets.Unprotect(protectedOnce).ShouldBe(secret);

        var otherKey = new MailboxCredentialProtector(Options.Create(new MailboxOptions { CredentialsKey = "alta-cheie" }));
        Should.Throw<System.Security.Cryptography.CryptographicException>(() => otherKey.Unprotect(protectedOnce));

        var noKey = new MailboxCredentialProtector(Options.Create(new MailboxOptions { Provider = MailboxOptions.MigaduProvider }));
        Should.Throw<InvalidOperationException>(() => noKey.Protect(secret));
    }

    // ---------- Ajutoare ----------

    private Guid Client(
        string firstName,
        string lastName,
        SignaturePacketStatus signatures = SignaturePacketStatus.Completed,
        ArrFleetStatus? arrFleet = null)
    {
        var user = new User { Id = Guid.NewGuid(), Email = $"client{_db.PfaRegistrations.Local.Count + 1}@example.ro", FirstName = firstName, LastName = lastName };
        var registration = new PfaRegistration
        {
            Id = Guid.NewGuid(), UserId = user.Id, User = user, FullName = $"{firstName} {lastName}",
            // Ordinea în coadă e cea a dosarelor.
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(_db.PfaRegistrations.Local.Count),
        };
        registration.SignaturePacket = new OnboardingSignaturePacket { Id = Guid.NewGuid(), PfaRegistrationId = registration.Id, Status = signatures };
        if (arrFleet is { } status)
        {
            registration.ArrFleetApplication = new ArrFleetApplication { Id = Guid.NewGuid(), PfaRegistrationId = registration.Id, UserId = user.Id, Status = status };
        }

        _db.PfaRegistrations.Add(registration);
        _db.SaveChanges();
        return registration.Id;
    }

    private async Task Process(IMailboxProvider? provider = null)
    {
        var provisioner = new ClientMailboxProvisioner(_db, provider ?? _fake, _secrets, _options);
        (await new ProcessClientMailboxesCommandHandler(_db, provisioner, _options).Handle(new ProcessClientMailboxesCommand(), default)).IsSuccess.ShouldBeTrue();
    }

    private TransferClientMailboxCommandHandler Transfer() =>
        new(_db, new FixedUser(_admin), _fake, _secrets, new RegisterExporter(), _files);

    private Task<List<ClientMailboxAuditAction>> Actions(ClientMailbox mailbox) =>
        _db.ClientMailboxAuditLogs.Where(l => l.ClientMailboxId == mailbox.Id).OrderBy(l => l.PerformedAtUtc).Select(l => l.Action).ToListAsync();

    /// <summary>Furnizorul real pentru tot, mai puțin crearea identității, care pică de câteva ori.</summary>
    private sealed class FailingIdentities(FakeMailboxProvider inner) : IMailboxProvider
    {
        public int Failures { get; set; }

        public Task<bool> MailboxExistsAsync(string localPart, CancellationToken ct) => inner.MailboxExistsAsync(localPart, ct);

        public Task<MailboxResult> CreateMailboxAsync(CreateMailboxRequest req, CancellationToken ct) => inner.CreateMailboxAsync(req, ct);

        public Task SetMailboxPasswordAsync(string localPart, string password, CancellationToken ct) => inner.SetMailboxPasswordAsync(localPart, password, ct);

        public Task SetRecoveryEmailAsync(string localPart, string email, CancellationToken ct) => inner.SetRecoveryEmailAsync(localPart, email, ct);

        public Task<IdentityResult> CreateIdentityAsync(string mailboxLocalPart, CreateIdentityRequest req, CancellationToken ct) =>
            Failures-- > 0
                ? throw new MailboxProviderException("Migadu nu răspunde la „creare identitate”.")
                : inner.CreateIdentityAsync(mailboxLocalPart, req, ct);

        public Task DeleteIdentityAsync(string mailboxLocalPart, string identityLocalPart, CancellationToken ct) => inner.DeleteIdentityAsync(mailboxLocalPart, identityLocalPart, ct);

        public Task<MailboxUsage> GetDomainUsageAsync(CancellationToken ct) => inner.GetDomainUsageAsync(ct);
    }

    private sealed class FixedUsage(MailboxUsage usage) : IMailboxProvider
    {
        public Task<MailboxUsage> GetDomainUsageAsync(CancellationToken ct) => Task.FromResult(usage);

        public Task<bool> MailboxExistsAsync(string localPart, CancellationToken ct) => throw new NotSupportedException();

        public Task<MailboxResult> CreateMailboxAsync(CreateMailboxRequest req, CancellationToken ct) => throw new NotSupportedException();

        public Task SetMailboxPasswordAsync(string localPart, string password, CancellationToken ct) => throw new NotSupportedException();

        public Task SetRecoveryEmailAsync(string localPart, string email, CancellationToken ct) => throw new NotSupportedException();

        public Task<IdentityResult> CreateIdentityAsync(string mailboxLocalPart, CreateIdentityRequest req, CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteIdentityAsync(string mailboxLocalPart, string identityLocalPart, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FixedUser(Guid id) : IUserContext
    {
        public Guid UserId => id;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
