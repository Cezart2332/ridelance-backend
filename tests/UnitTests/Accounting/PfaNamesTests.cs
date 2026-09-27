using Application.Accounting.Declarations;
using Application.Accounting.Pfas;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Accounting.Anaf;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>Denumirea PFA-ului (<c>den</c>) când certificatul de înregistrare n-a fost citit.</summary>
public sealed class PfaNamesTests
{
    [Theory]
    [InlineData("POPESCU ION PERSOANĂ FIZICĂ AUTORIZATĂ", "Popescu Ion", "Ion Popescu", "Ion", "Popescu", "POPESCU ION PERSOANĂ FIZICĂ AUTORIZATĂ")]
    [InlineData("", "POPESCU ION", "Ion Popescu", "Ion", "Popescu", "POPESCU ION PFA")]
    [InlineData("   ", null, "Ion Popescu", "Ion", "Popescu", "Ion Popescu PFA")]
    [InlineData(null, null, "", "test", "testr", "testr test PFA")]
    [InlineData(null, "Popescu Ion PFA", null, null, null, "Popescu Ion PFA")]
    [InlineData(null, null, null, null, null, "")]
    public void Name_uses_the_registration_certificate_else_the_holder_name_plus_PFA(
        string? legalName, string? holderName, string? fullName, string? firstName, string? lastName, string expected) =>
        PfaNames.Of(legalName, holderName, fullName, firstName, lastName).ShouldBe(expected);

    [Fact]
    public async Task Declaration_header_has_a_name_when_the_legal_name_is_empty()
    {
        // Cazul din producție: certificat necitit, formularul fără nume complet → den="" și
        // „Lipsește denumirea PFA-ului.” la orice declarație.
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new Events());
        var user = new User { Id = Guid.NewGuid(), Email = "test@example.ro", FirstName = "test", LastName = "testr", Role = UserRole.Client };
        var pfa = new PfaRegistration
        {
            Id = Guid.NewGuid(), UserId = user.Id, User = user, LegalName = "", FullName = null, Cui = "47742006",
            Street = "Test", Number = "123", City = "Constanta", County = "Constanta",
        };
        db.Users.Add(user);
        db.PfaRegistrations.Add(pfa);
        await db.SaveChangesAsync();

        Application.Abstractions.Anaf.AnafTaxpayer taxpayer = (await new DeclarationFiles(db, new AnafDeclarationXmlService(), new MemoryFiles(), new PlainSecrets())
            .TaxpayerAsync(pfa.Id, CancellationToken.None))!;

        taxpayer.Name.ShouldBe("testr test PFA");
        DeclarationValidator.CheckTaxpayer(Domain.Accounting.DeclarationType.D100, taxpayer).ShouldNotContain(message => message.Field == "den");
    }

    private sealed class Events : Infrastructure.DomainEvents.IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<SharedKernel.IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
