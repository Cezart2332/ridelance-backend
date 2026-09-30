using Application.Abstractions.Security;
using Domain.Banking;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.BackgroundJobs;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Banking;

/// <summary>IBAN-urile salvate mascate devin complete, din IBAN-ul criptat al declarației contului.</summary>
public sealed class IbanBackfillTests
{
    [Fact]
    public async Task MaskedIbansAreRecoveredFromTheEncryptedDeclaration()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Events());
        var user = new User { Id = Guid.NewGuid(), Email = "ion@example.test", FirstName = "Ion", LastName = "Popescu" };
        var pfa = new PfaRegistration { Id = Guid.NewGuid(), UserId = user.Id, User = user, FullName = "Ion Popescu" };
        var connection = new BankConnection { Id = Guid.NewGuid(), UserId = user.Id, Provider = "test", InstitutionId = "BT", ProviderConsentId = "c", Status = BankConnectionStatus.Linked };
        var account = new BankAccount { Id = Guid.NewGuid(), BankConnectionId = connection.Id, UserId = user.Id, ProviderAccountId = "acc", Iban = "RO49••••0000", IsActive = true };
        var other = new BankAccount { Id = Guid.NewGuid(), BankConnectionId = connection.Id, UserId = user.Id, ProviderAccountId = "eur", Iban = "RO11••••9999", IsActive = true };
        var protector = new Plain();
        db.Users.Add(user);
        db.PfaRegistrations.Add(pfa);
        db.BankConnections.Add(connection);
        db.BankAccounts.AddRange(account, other);
        db.PfaBankAccountDeclarations.Add(new PfaBankAccountDeclaration
        {
            Id = Guid.NewGuid(), PfaRegistrationId = pfa.Id, BankConnectionId = connection.Id,
            Iban = "RO49••••0000", IbanEncrypted = protector.Protect("RO49 AAAA 1B31 0075 9384 0000"),
        });
        await db.SaveChangesAsync();

        (await IbanBackfillJob.BackfillAsync(db, protector, default)).ShouldBe((1, 1));
        (await db.PfaBankAccountDeclarations.SingleAsync()).Iban.ShouldBe("RO49AAAA1B31007593840000");
        account.Iban.ShouldBe("RO49AAAA1B31007593840000");
        other.Iban.ShouldBe("RO11••••9999"); // fără IBAN complet cunoscut: rămâne până la reconectare

        (await IbanBackfillJob.BackfillAsync(db, protector, default)).ShouldBe((0, 0));
    }

    private sealed class Plain : ISecretProtector
    {
        public string Protect(string plainText) => $"enc:{plainText}";

        public string Unprotect(string protectedText) => protectedText["enc:".Length..];
    }

    private sealed class Events : Infrastructure.DomainEvents.IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
