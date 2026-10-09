using Application.Abstractions.Authentication;
using Application.Accounting.Declarations;
using Application.Payments;
using Domain.Payments;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Payments;

/// <summary>PFAlone își ține singur evidența; PFA Full e al contabilului.</summary>
public sealed class PlanAccessTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    public void Dispose() => _db.Dispose();

    private (Guid UserId, Guid PfaId) Client(SubscriptionPlan plan, SubscriptionStatus status = SubscriptionStatus.Active)
    {
        var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@ridelance.ro", FirstName = "Ion", LastName = "Popescu" };
        var pfa = new PfaRegistration { Id = Guid.NewGuid(), UserId = user.Id, User = user, FullName = "Ion Popescu", Cui = "12345674" };
        _db.PfaRegistrations.Add(pfa);
        _db.UserSubscriptions.Add(new UserSubscription { Id = Guid.NewGuid(), UserId = user.Id, Plan = plan, Status = status });
        _db.SaveChanges();
        return (user.Id, pfa.Id);
    }

    [Fact]
    public async Task OnlyActivePfaAlone_LeavesTheAccountantsScope()
    {
        (Guid _, Guid alone) = Client(SubscriptionPlan.PfaAlone);
        (Guid _, Guid full) = Client(SubscriptionPlan.PfaFull);
        (Guid _, Guid cancelledAlone) = Client(SubscriptionPlan.PfaAlone, SubscriptionStatus.Cancelled);

        List<Guid> selfManaged = await PlanAccess.SelfManagedPfaIds(_db).ToListAsync();

        selfManaged.ShouldBe([alone]);
        selfManaged.ShouldNotContain(full);
        selfManaged.ShouldNotContain(cancelledAlone);
    }

    [Fact]
    public async Task DeclarationGenerator_IsOnlyForPfaAlone()
    {
        (Guid full, Guid _) = Client(SubscriptionPlan.PfaFull);
        (Guid alone, Guid _) = Client(SubscriptionPlan.PfaAlone);

        Result<IReadOnlyList<OwnDeclarationDto>> refused = await new GetOwnDeclarationsQueryHandler(_db, new FixedUser(full))
            .Handle(new GetOwnDeclarationsQuery(2026), default);
        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("OwnDeclarations.NotSelfManaged");

        Result<IReadOnlyList<OwnDeclarationDto>> allowed = await new GetOwnDeclarationsQueryHandler(_db, new FixedUser(alone))
            .Handle(new GetOwnDeclarationsQuery(2026), default);
        allowed.IsSuccess.ShouldBeTrue();
        allowed.Value.ShouldBeEmpty();
    }

    private sealed class FixedUser(Guid id) : IUserContext
    {
        public Guid UserId => id;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<SharedKernel.IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
