using Application.Accounting;
using Application.Accounting.Declarations;
using Application.Accounting.Spv;
using Domain.Accounting;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Accounting.Anaf;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>
/// SPV prin aplicația desktop: cheia aplicației, trimiterile (interval de recuperare, o singură
/// trimitere o dată, reluarea după PC închis), mesajele fără dubluri și recipisele legate de declarații.
/// </summary>
public sealed class SpvTests : IDisposable
{
    private const string Cui = "12345674";

    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly MemoryFiles _files = new();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _pfa;

    public SpvTests()
    {
        _db.Users.Add(new User { Id = _admin, Email = "admin@ridelance.ro", FirstName = "Admin", LastName = "RIDElance", Role = UserRole.Admin });
        var user = new User { Id = Guid.NewGuid(), Email = "ion@ridelance.ro", FirstName = "Ion", LastName = "Popescu", Role = UserRole.Client };
        var pfa = new PfaRegistration { Id = Guid.NewGuid(), UserId = user.Id, User = user, FullName = "Ion Popescu", LegalName = "POPESCU ION PFA", Cui = Cui };
        _pfa = pfa.Id;
        _db.PfaRegistrations.Add(pfa);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Only_a_live_admin_key_is_accepted()
    {
        SpvService spv = Service();
        (SpvAgentKey key, string secret) = (await spv.CreateKeyAsync(_admin, "Laptop birou", CancellationToken.None)).Value;

        secret.ShouldStartWith(SpvService.KeyPrefix);
        key.KeyHash.ShouldNotContain(secret);
        (await spv.AuthenticateAsync(secret, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await spv.AuthenticateAsync(secret + "x", CancellationToken.None)).Error.ShouldBe(SpvErrors.InvalidKey);

        await spv.RevokeKeyAsync(key.Id, _admin, CancellationToken.None);
        (await spv.AuthenticateAsync(secret, CancellationToken.None)).Error.ShouldBe(SpvErrors.InvalidKey);
    }

    [Theory]
    [InlineData(null, 60)]
    [InlineData(0.3, 3)]
    [InlineData(3.0, 5)]
    [InlineData(59.0, 60)]
    [InlineData(200.0, 60)]
    public void The_interval_covers_the_time_since_the_last_success_with_two_days_overlap(double? daysAgo, int expected)
    {
        var now = new DateTime(2026, 9, 29, 8, 0, 0, DateTimeKind.Utc);
        SpvService.DaysSince(daysAgo is { } days ? now.AddDays(-days) : null, now).ShouldBe(expected);
    }

    [Fact]
    public async Task One_run_at_a_time_and_a_run_left_open_by_a_closed_pc_is_released()
    {
        SpvService spv = Service();
        SpvAgentKey key = await KeyAsync(spv);
        await spv.QueueRequestAsync(_pfa, "VECTOR FISCAL", null, _admin, CancellationToken.None);

        SpvRunStart first = (await spv.StartRunAsync(key, "PC-1", "1.0", CancellationToken.None)).Value;
        first.Days.ShouldBe(60);
        first.Cuis.ShouldBe([Cui]);
        first.Requests.ShouldHaveSingleItem().Type.ShouldBe("VECTOR FISCAL");
        (await spv.StartRunAsync(key, "PC-2", "1.0", CancellationToken.None)).Error.ShouldBe(SpvErrors.RunInProgress);

        // PC-ul s-a închis în timpul trimiterii: după termen, trimiterea se abandonează, cererea revine.
        SpvSyncRun open = await _db.SpvSyncRuns.SingleAsync();
        open.LeaseUntilUtc = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        SpvRunStart second = (await spv.StartRunAsync(key, "PC-1", "1.0", CancellationToken.None)).Value;
        second.Requests.ShouldHaveSingleItem().Type.ShouldBe("VECTOR FISCAL");
        (await _db.SpvSyncRuns.SingleAsync(r => r.Id == first.RunId)).Status.ShouldBe(SpvSyncRunStatus.Abandoned);
        second.Days.ShouldBe(60);

        await spv.FinishRunAsync(key, second.RunId, null, CancellationToken.None);
        (await spv.StartRunAsync(key, "PC-1", "1.0", CancellationToken.None)).Value.Days.ShouldBe(3);
    }

    [Fact]
    public async Task A_message_is_kept_once_and_only_new_ids_are_asked_for()
    {
        SpvService spv = Service();
        SpvAgentKey key = await KeyAsync(spv);
        Guid run = (await spv.StartRunAsync(key, "PC-1", "1.0", CancellationToken.None)).Value.RunId;
        var message = new SpvIncomingMessage("900", Cui, "NOTIFICARE", new DateTime(2026, 9, 20, 7, 0, 0, DateTimeKind.Utc), null, "Notificare de conformare");

        await spv.ReceiveAsync(key, run, message, Pdf("n1"), CancellationToken.None);
        await spv.ReceiveAsync(key, run, message, Pdf("n1"), CancellationToken.None);

        SpvMessage stored = await _db.SpvMessages.SingleAsync();
        stored.PfaRegistrationId.ShouldBe(_pfa);
        stored.Status.ShouldBe(SpvMessageStatus.New);
        stored.DocumentId.ShouldNotBeNull();
        (await spv.NewIdsAsync(key, run, ["900", "901", "901"], CancellationToken.None)).Value.ShouldBe(["901"]);

        // Același document sub alt mesaj: un singur fișier păstrat.
        await spv.ReceiveAsync(key, run, message with { Id = "901" }, Pdf("n1"), CancellationToken.None);
        (await _db.SpvMessages.Select(m => m.DocumentId).Distinct().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_message_for_an_unknown_cif_needs_attention()
    {
        SpvService spv = Service();
        SpvAgentKey key = await KeyAsync(spv);
        Guid run = (await spv.StartRunAsync(key, "PC-1", "1.0", CancellationToken.None)).Value.RunId;

        await spv.ReceiveAsync(key, run, new SpvIncomingMessage("902", "RO41000105", "NOTIFICARE", DateTime.UtcNow, null, null), Pdf("x"), CancellationToken.None);

        SpvMessage stored = await _db.SpvMessages.SingleAsync();
        stored.Status.ShouldBe(SpvMessageStatus.NeedsAttention);
        stored.Note.ShouldBe("Niciun client cu CIF 41000105.");
    }

    [Fact]
    public async Task A_recipisa_is_attached_to_the_submitted_declaration()
    {
        Guid version = Declaration(DeclarationType.D100, "2026-08", DeclarationStatus.Submitted);
        SpvService spv = Service();
        SpvAgentKey key = await KeyAsync(spv);
        Guid run = (await spv.StartRunAsync(key, "PC-1", "1.0", CancellationToken.None)).Value.RunId;

        await spv.ReceiveAsync(
            key,
            run,
            new SpvIncomingMessage("903", Cui, "RECIPISA", DateTime.UtcNow, "7001", "recipisa pentru CIF 12345674, tip D100, numar_inregistrare INTERNT-123456789-2026/10-09-2026, perioada raportare 8.2026"),
            Pdf("recipisa"),
            CancellationToken.None);

        DeclarationVersion accepted = await _db.DeclarationVersions.SingleAsync(v => v.Id == version);
        accepted.Status.ShouldBe(DeclarationStatus.Accepted);
        accepted.ReceiptNumber.ShouldBe("INTERNT-123456789-2026/10-09-2026");
        accepted.ReceiptDocumentId.ShouldNotBeNull();
        SpvMessage stored = await _db.SpvMessages.SingleAsync();
        stored.Status.ShouldBe(SpvMessageStatus.Processed);
        stored.DeclarationVersionId.ShouldBe(version);
    }

    [Fact]
    public async Task A_recipisa_without_a_declaration_needs_attention()
    {
        SpvService spv = Service();
        SpvAgentKey key = await KeyAsync(spv);
        Guid run = (await spv.StartRunAsync(key, "PC-1", "1.0", CancellationToken.None)).Value.RunId;

        await spv.ReceiveAsync(key, run, new SpvIncomingMessage("904", Cui, "RECIPISA", DateTime.UtcNow, null, "recipisa pentru CIF 12345674, tip D301, perioada raportare 07.2026"), Pdf("r"), CancellationToken.None);

        SpvMessage stored = await _db.SpvMessages.SingleAsync();
        stored.Status.ShouldBe(SpvMessageStatus.NeedsAttention);
        stored.Note.ShouldBe("Recipisă D301 2026-07, fără declarație în RIDElance.");
    }

    [Fact]
    public async Task A_request_goes_out_with_the_next_run_and_its_answer_closes_it()
    {
        SpvService spv = Service();
        SpvAgentKey key = await KeyAsync(spv);
        (await spv.QueueRequestAsync(_pfa, "Fisa Rol", new Dictionary<string, string> { ["an"] = "2026", ["ceva"] = "x" }, _admin, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await spv.QueueRequestAsync(_pfa, "Orice", null, _admin, CancellationToken.None)).Error.ShouldBe(SpvErrors.UnknownType);

        SpvRunStart run = (await spv.StartRunAsync(key, "PC-1", "1.0", CancellationToken.None)).Value;
        SpvRequestToSend request = run.Requests.ShouldHaveSingleItem();
        request.Parameters.ShouldBe(new Dictionary<string, string> { ["an"] = "2026" });

        await spv.RequestResultAsync(key, run.RunId, request.Id, "5555", null, CancellationToken.None);
        await spv.ReceiveAsync(key, run.RunId, new SpvIncomingMessage("905", Cui, "RASPUNS SOLICITARE", DateTime.UtcNow, "5555", "Fisa Rol"), Pdf("fisa"), CancellationToken.None);

        SpvRequest answered = await _db.SpvRequests.SingleAsync();
        answered.Status.ShouldBe(SpvRequestStatus.Answered);
        (await _db.SpvMessages.SingleAsync()).SpvRequestId.ShouldBe(answered.Id);
    }

    [Theory]
    [InlineData("recipisa pentru CIF 12345674, tip D100, numar_inregistrare INTERNT-1-2026/10-09-2026, perioada raportare 8.2026", "D100", "2026-08", "INTERNT-1-2026/10-09-2026")]
    [InlineData("Recipisa: tip D301 perioada de raportare 12.2025", "D301", "2025-12", null)]
    [InlineData("recipisa pentru CIF 12345674, tip D700, numar_inregistrare INTERNT-9", "D700", null, "INTERNT-9")]
    public void Recipisa_details_are_read_whatever_their_order(string details, string type, string? period, string? number)
    {
        SpvRecipisaInfo info = SpvRecipisa.Read(details)!;
        info.DeclarationType.ShouldBe(type);
        info.Period.ShouldBe(period);
        info.RegistrationNumber.ShouldBe(number);
    }

    private SpvService Service()
    {
        var xml = new AnafDeclarationXmlService();
        var files = new DeclarationFiles(_db, xml, _files, new PlainSecrets());
        var validator = new DeclarationValidator(_db, xml, new FakeAnafValidator(), files, Options.Create(new AccountingOptions()));
        return new SpvService(_db, files, new DeclarationActions(_db, files, validator, Options.Create(new AccountingOptions())));
    }

    private async Task<SpvAgentKey> KeyAsync(SpvService spv)
    {
        string secret = (await spv.CreateKeyAsync(_admin, "Laptop", CancellationToken.None)).Value.Secret;
        return (await spv.AuthenticateAsync(secret, CancellationToken.None)).Value;
    }

    private Guid Declaration(DeclarationType type, string period, DeclarationStatus status)
    {
        var declaration = new Declaration { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Period = period, Type = type };
        var version = new DeclarationVersion { Id = Guid.NewGuid(), DeclarationId = declaration.Id, Declaration = declaration, VersionNo = 1, Status = status };
        _db.Declarations.Add(declaration);
        _db.DeclarationVersions.Add(version);
        _db.SaveChanges();
        return version.Id;
    }

    private static SpvIncomingFile Pdf(string content) => new($"{content}.pdf", "application/pdf", System.Text.Encoding.UTF8.GetBytes($"%PDF-1.4 {content}"));

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
