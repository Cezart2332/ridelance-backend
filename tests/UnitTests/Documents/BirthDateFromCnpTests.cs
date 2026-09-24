using Application.Abstractions.Security;
using Application.Documents.ExtractedFields;
using Domain.Documents;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Documents;

/// <summary>
/// Data nașterii din buletin se ia din CNP când acesta e valid: pe buletinul vechi nu e tipărită,
/// iar modelul o deducea greșit — formularul de înființare arăta apoi că nu se potrivește cu CNP-ul.
/// </summary>
public sealed class BirthDateFromCnpTests
{
    [Fact]
    public async Task Un_CNP_valid_inlocuieste_data_nasterii_dedusa_de_model()
    {
        var userId = Guid.NewGuid();
        using ApplicationDbContext db = NewDb();
        var document = new Document { Id = Guid.NewGuid(), UserId = userId, Category = DocumentCategory.CarteIdentitate };
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var applier = new ExtractedFieldApplier(db, new PassThroughProtector());
        // Ordinea din catalog: întâi data nașterii (greșită), apoi CNP-ul (5 = născut între 2000 și 2099).
        await applier.ApplyAsync(document, "date_of_birth", "1996-09-24", CancellationToken.None);
        await applier.ApplyAsync(document, "cnp", "5010519420017", CancellationToken.None);
        await db.SaveChangesAsync();

        (await db.OnboardingEligibilityProfiles.SingleAsync(p => p.UserId == userId))
            .DateOfBirth.ShouldBe(new DateOnly(2001, 5, 19));
    }

    [Fact]
    public async Task Un_CNP_invalid_lasa_data_citita()
    {
        var userId = Guid.NewGuid();
        using ApplicationDbContext db = NewDb();
        var document = new Document { Id = Guid.NewGuid(), UserId = userId, Category = DocumentCategory.CarteIdentitate };
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var applier = new ExtractedFieldApplier(db, new PassThroughProtector());
        await applier.ApplyAsync(document, "date_of_birth", "1996-09-24", CancellationToken.None);
        // Cifra de control greșită: nu e un CNP, deci nu decide nimic.
        await applier.ApplyAsync(document, "cnp", "5010519420010", CancellationToken.None);
        await db.SaveChangesAsync();

        (await db.OnboardingEligibilityProfiles.SingleAsync(p => p.UserId == userId))
            .DateOfBirth.ShouldBe(new DateOnly(1996, 9, 24));
    }

    private static ApplicationDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new Events());

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
