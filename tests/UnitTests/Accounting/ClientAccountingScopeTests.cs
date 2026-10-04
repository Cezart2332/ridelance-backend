using Application.Accounting.Pfas;
using Application.Accounting.Spv;
using Domain.Accounting;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

public sealed class ClientAccountingScopeTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    [Fact]
    public async Task Owner_is_resolved_from_user_not_another_pfa()
    {
        PfaRegistration own = AddPfa();
        AddPfa();
        await _db.SaveChangesAsync();
        (await ClientAccountingScope.PfaIdAsync(_db, own.UserId, CancellationToken.None)).ShouldBe(own.Id);
        (await ClientAccountingScope.PfaIdAsync(_db, Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Deleted_owner_has_no_scope()
    {
        PfaRegistration own = AddPfa();
        own.User.DeletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        (await ClientAccountingScope.PfaIdAsync(_db, own.UserId, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Invoice_files_are_restricted_to_the_resolved_owner()
    {
        PfaRegistration own = AddPfa();
        PfaRegistration other = AddPfa();
        var message = new EFacturaMessage { Id = Guid.NewGuid(), PfaRegistrationId = other.Id };
        _db.EFacturaMessages.Add(message);
        await _db.SaveChangesAsync();
        (await ClientAccountingScope.OwnsInvoiceAsync(_db, own.Id, message.Id, CancellationToken.None)).ShouldBeFalse();
        (await ClientAccountingScope.OwnsInvoiceAsync(_db, other.Id, message.Id, CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task Spv_files_require_a_link_even_when_cif_matches()
    {
        PfaRegistration own = AddPfa();
        PfaRegistration other = AddPfa();
        var linked = new SpvMessage { Id = Guid.NewGuid(), PfaRegistrationId = own.Id };
        var foreign = new SpvMessage { Id = Guid.NewGuid(), PfaRegistrationId = other.Id, Cif = own.Cui! };
        var unlinked = new SpvMessage { Id = Guid.NewGuid(), Cif = own.Cui! };
        _db.SpvMessages.AddRange(linked, foreign, unlinked);
        await _db.SaveChangesAsync();
        (await ClientAccountingScope.OwnsSpvMessageAsync(_db, own.Id, linked.Id, CancellationToken.None)).ShouldBeTrue();
        (await ClientAccountingScope.OwnsSpvMessageAsync(_db, own.Id, foreign.Id, CancellationToken.None)).ShouldBeFalse();
        (await ClientAccountingScope.OwnsSpvMessageAsync(_db, own.Id, unlinked.Id, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task Owner_spv_view_excludes_other_messages_and_requests()
    {
        PfaRegistration own = AddPfa();
        PfaRegistration other = AddPfa();
        var linked = new SpvMessage { Id = Guid.NewGuid(), PfaRegistrationId = own.Id };
        _db.SpvMessages.AddRange(linked,
            new SpvMessage { Id = Guid.NewGuid(), PfaRegistrationId = other.Id, Cif = own.Cui! },
            new SpvMessage { Id = Guid.NewGuid(), Cif = own.Cui! });
        _db.SpvRequests.AddRange(new SpvRequest { Id = Guid.NewGuid(), PfaRegistrationId = own.Id },
            new SpvRequest { Id = Guid.NewGuid(), PfaRegistrationId = other.Id });
        await _db.SaveChangesAsync();
        PfaSpvDto view = (await new GetPfaSpvQueryHandler(_db).Handle(new GetPfaSpvQuery(own.Id, LinkedOnly: true), CancellationToken.None)).Value;
        view.Messages.ShouldHaveSingleItem().Id.ShouldBe(linked.Id);
        view.Requests.ShouldHaveSingleItem();
    }

    private PfaRegistration AddPfa()
    {
        var user = new User { Id = Guid.NewGuid(), Role = UserRole.Client };
        var pfa = new PfaRegistration { Id = Guid.NewGuid(), UserId = user.Id, User = user, Cui = "12345678" };
        _db.Users.Add(user);
        _db.PfaRegistrations.Add(pfa);
        return pfa;
    }
    public void Dispose() => _db.Dispose();
    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
