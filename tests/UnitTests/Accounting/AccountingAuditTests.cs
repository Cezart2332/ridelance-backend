using Application.Abstractions.Authentication;
using Application.Accounting;
using Application.Accounting.Audit;
using Application.Accounting.Contracts;
using Domain.Accounting;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Accounting;
using Infrastructure.Authentication;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>B5: auditul automat al înregistrărilor contabile și jurnalul unui PFA.</summary>
public sealed class AccountingAuditTests : IDisposable
{
    private readonly Guid _accountant = Guid.NewGuid();
    private readonly Guid _pfa = Guid.NewGuid();
    private readonly ApplicationDbContext _db;

    public AccountingAuditTests()
    {
        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .AddInterceptors(new AccountingAuditInterceptor(new FixedUser(_accountant)))
                .Options,
            new Events());
        var user = new User { Id = Guid.NewGuid(), Email = "ion@ridelance.ro", FirstName = "Ion", LastName = "Popescu" };
        _db.Users.Add(new User { Id = _accountant, Email = "contabil@ridelance.ro", FirstName = "Contabil", LastName = "RIDElance", Role = UserRole.Contabil });
        _db.PfaRegistrations.Add(new PfaRegistration { Id = _pfa, UserId = user.Id, User = user, FullName = "Ion Popescu" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Changes_to_accounting_records_are_audited_with_old_and_new_values()
    {
        var setting = new PfaAccountingSetting
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = _pfa,
            Key = PfaAccountingSettingKeys.Art317,
            ValueJson = "true",
            ValidFrom = new DateOnly(2026, 1, 1),
            Note = "Cod primit",
            ChangedByUserId = _accountant,
        };
        _db.PfaAccountingSettings.Add(setting);
        await _db.SaveChangesAsync();

        setting.Note = "Cod verificat SPV";
        await _db.SaveChangesAsync();

        List<AuditLog> logs = await _db.AuditLogs.OrderBy(a => a.AtUtc).ToListAsync();
        logs.Select(a => (a.Entity, a.Action, a.PfaRegistrationId, a.UserId)).ShouldBe(
        [
            (nameof(PfaAccountingSetting), "CREATE", (Guid?)_pfa, (Guid?)_accountant),
            (nameof(PfaAccountingSetting), "UPDATE", _pfa, _accountant),
        ]);
        logs[0].AfterJson!.ShouldContain("\"key\":\"art317\"");
        (logs[1].BeforeJson, logs[1].AfterJson).ShouldBe(("{\"note\":\"Cod primit\"}", "{\"note\":\"Cod verificat SPV\"}"));
    }

    [Fact]
    public async Task An_explicit_audit_is_not_duplicated()
    {
        var rate = new VatRate { Id = Guid.NewGuid(), Rate = 21, ValidFrom = new DateOnly(2025, 8, 1) };
        _db.VatRates.Add(rate);
        AccountingAudit.Record(_db, null, nameof(VatRate), rate.Id, "CREATE_RULE", null, new { rate = 21 }, "Cota nouă", _accountant);
        await _db.SaveChangesAsync();

        (await _db.AuditLogs.Select(a => a.Action).ToListAsync()).ShouldBe(["CREATE_RULE"]);
    }

    [Fact]
    public async Task Skipped_records_and_unchanged_saves_leave_no_audit()
    {
        _db.ExchangeRates.Add(new ExchangeRate { Id = Guid.NewGuid(), Currency = "EUR", Date = new DateOnly(2026, 8, 31), Rate = 4.97m, Source = "BNR" });
        await _db.SaveChangesAsync();
        await _db.SaveChangesAsync();

        (await _db.AuditLogs.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task System_changes_have_no_user()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .AddInterceptors(new AccountingAuditInterceptor(new NoUser()))
                .Options,
            new Events());
        db.VatRates.Add(new VatRate { Id = Guid.NewGuid(), Rate = 21, ValidFrom = new DateOnly(2025, 8, 1) });
        await db.SaveChangesAsync();

        (await db.AuditLogs.SingleAsync()).UserId.ShouldBeNull();
    }

    [Fact]
    public async Task Pfa_audit_lists_the_newest_entries_with_their_author()
    {
        AccountingAudit.Record(_db, _pfa, nameof(DeclarationVersion), Guid.NewGuid(), "MARK_SIGNED", new { status = "READY_TO_SIGN" }, new { status = "SIGNED" }, null, _accountant);
        await _db.SaveChangesAsync();
        AccountingAudit.Record(_db, _pfa, nameof(PlatformDocument), Guid.NewGuid(), "EDIT", null, null, "Suma corectată", null);
        await _db.SaveChangesAsync();

        var handler = new ListPfaAuditQueryHandler(_db);
        IReadOnlyList<AuditEntryDto> all = (await handler.Handle(new ListPfaAuditQuery(_pfa, null, null, null), CancellationToken.None)).Value;
        IReadOnlyList<AuditEntryDto> declarations = (await handler.Handle(new ListPfaAuditQuery(_pfa, null, null, nameof(DeclarationVersion)), CancellationToken.None)).Value;
        IReadOnlyList<AuditEntryDto> tomorrow = (await handler.Handle(
            new ListPfaAuditQuery(_pfa, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), null, null), CancellationToken.None)).Value;

        all.Select(a => (a.Action, a.User.Name)).ShouldBe([("EDIT", "Sistem"), ("MARK_SIGNED", "Contabil RIDElance")]);
        declarations.ShouldHaveSingleItem().After!.Value.GetProperty("status").GetString().ShouldBe("SIGNED");
        tomorrow.ShouldBeEmpty();
        (await handler.Handle(new ListPfaAuditQuery(Guid.NewGuid(), null, null, null), CancellationToken.None)).Error.ShouldBe(AccountingErrors.PfaNotFound);
    }

    private sealed class FixedUser(Guid id) : IUserContext
    {
        public Guid UserId => id;
    }

    private sealed class NoUser : IUserContext
    {
        public Guid UserId => throw new UserContextUnavailableException();
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
