using Application.Abstractions.Authentication;
using Application.Accounting;
using Application.Accounting.Pfas;
using Domain.Accounting;
using Domain.Banking;
using Domain.Chat;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>„Clienți PFA” și „De făcut azi”: portofoliul pe rol, onboardingul, banca și mesajele.</summary>
public sealed class ClientWorkspaceTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

    private readonly Guid _accountant = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();

    public ClientWorkspaceTests()
    {
        _db.Users.Add(new User { Id = _accountant, Email = "contabil@ridelance.ro", FirstName = "Ana", LastName = "Contabil", Role = UserRole.Contabil });
        _db.Users.Add(new User { Id = _admin, Email = "admin@ridelance.ro", FirstName = "Admin", LastName = "RIDElance", Role = UserRole.Admin });
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Accountant_sees_the_assigned_clients_with_bank_and_unread_messages()
    {
        Guid ion = Pfa("Ion", "Popescu", "12345674", _accountant, onboarded: true);
        Guid ana = Pfa("Ana", "Georgescu", "41000075", _accountant, onboarded: false);
        Pfa("Altul", "Client", "41000083", null, onboarded: true);
        await _db.SaveChangesAsync();
        Guid ionUser = await _db.PfaRegistrations.Where(p => p.Id == ion).Select(p => p.UserId).SingleAsync();
        _db.BankConnections.Add(new BankConnection { Id = Guid.NewGuid(), UserId = ionUser, Status = BankConnectionStatus.Linked, CreatedAtUtc = DateTime.UtcNow });
        var room = new ChatRoom { Id = Guid.NewGuid(), ClientUserId = ionUser, ProfessionalUserId = _accountant };
        _db.ChatRooms.Add(room);
        _db.ChatMessages.AddRange(
            new ChatMessage { Id = Guid.NewGuid(), ChatRoomId = room.Id, SenderId = ionUser, Content = "Am încărcat factura", IsRead = false },
            new ChatMessage { Id = Guid.NewGuid(), ChatRoomId = room.Id, SenderId = ionUser, Content = "Mersi", IsRead = true },
            new ChatMessage { Id = Guid.NewGuid(), ChatRoomId = room.Id, SenderId = _accountant, Content = "Primit", IsRead = false });
        await _db.SaveChangesAsync();

        IReadOnlyList<ClientWorkspaceRow> rows = await List(_accountant);

        rows.Select(r => r.PfaId).ShouldBe([ana, ion]);
        ClientWorkspaceRow ionRow = rows.Single(r => r.PfaId == ion);
        (ionRow.Stage, ionRow.MonthStatus, ionRow.BankStatus, ionRow.UnreadMessages, ionRow.Name)
            .ShouldBe((ClientStage.Active, (PfaMonthStatus?)PfaMonthStatus.NotProcessed, "LINKED", 1, "POPESCU ION PFA"));
        // Ca celelalte stări ale modulului: text UPPER_SNAKE, nu numere.
        string json = System.Text.Json.JsonSerializer.Serialize(ionRow, Web);
        json.ShouldContain("\"stage\":\"ACTIVE\"");
        json.ShouldContain("\"bankStatus\":\"LINKED\"");
        ClientWorkspaceRow anaRow = rows.Single(r => r.PfaId == ana);
        (anaRow.Stage, anaRow.MonthStatus, anaRow.BankStatus).ShouldBe((ClientStage.Onboarding, (PfaMonthStatus?)null, (string?)null));
    }

    [Fact]
    public async Task Admin_sees_every_client()
    {
        Pfa("Ion", "Popescu", "12345674", _accountant, onboarded: true);
        Pfa("Altul", "Client", "41000083", null, onboarded: true);
        await _db.SaveChangesAsync();

        (await List(_admin)).Count.ShouldBe(2);
    }

    private async Task<IReadOnlyList<ClientWorkspaceRow>> List(Guid user) =>
        (await new ListClientWorkspaceQueryHandler(_db, new FixedUser(user))
            .Handle(new ListClientWorkspaceQuery("2026-08"), CancellationToken.None)).Value;

    private Guid Pfa(string firstName, string lastName, string cui, Guid? accountant, bool onboarded)
    {
        var user = new User { Id = Guid.NewGuid(), Email = $"{firstName}@exemplu.ro", FirstName = firstName, LastName = lastName, Role = UserRole.Client };
        var pfa = new PfaRegistration
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            LegalName = $"{lastName.ToUpperInvariant()} {firstName.ToUpperInvariant()} PFA",
            Cui = cui,
            AssignedContabilId = accountant,
            OnboardingCompletedAtUtc = onboarded ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null,
        };
        _db.Users.Add(user);
        _db.PfaRegistrations.Add(pfa);
        return pfa.Id;
    }

    private sealed class FixedUser(Guid id) : IUserContext
    {
        public Guid UserId => id;
    }

    private sealed class Events : Infrastructure.DomainEvents.IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
