using Application.PfaRegistrations.Onboarding;
using Domain.Documents;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>
/// Dosarele (ARR, copie conformă) se generează doar din acte verificate de un om.
///
/// Dosarul se depune la ghișeu în numele clientului. Înainte se genera din orice era încărcat —
/// inclusiv acte încă în verificare sau respinse — deci putea pleca la ARR un dosar pe care nu-l
/// văzuse nimeni.
/// </summary>
public sealed class DossierVerificationTests
{
    private static readonly Guid Client = Guid.NewGuid();

    private static readonly OnboardingSectionCatalog.DocumentRequirement[] Requirements =
    [
        new("Cazier judiciar", [DocumentCategory.CazierJudiciar]),
        new("Aviz medical", [DocumentCategory.AdeverintaMedicala]),
        new("Aviz psihologic", [DocumentCategory.AvizPsihologic]),
    ];

    [Fact]
    public async Task All_verified_lets_the_dossier_through()
    {
        using ApplicationDbContext db = await With(
            (DocumentCategory.CazierJudiciar, DocumentStatus.Verified),
            (DocumentCategory.AdeverintaMedicala, DocumentStatus.Verified),
            (DocumentCategory.AvizPsihologic, DocumentStatus.Verified));

        (await DossierAttachments.UnverifiedAsync(db, Client, Requirements, CancellationToken.None))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task A_document_still_in_review_blocks_it_and_is_named()
    {
        using ApplicationDbContext db = await With(
            (DocumentCategory.CazierJudiciar, DocumentStatus.Verified),
            (DocumentCategory.AdeverintaMedicala, DocumentStatus.Pending),
            (DocumentCategory.AvizPsihologic, DocumentStatus.Verified));

        (await DossierAttachments.UnverifiedAsync(db, Client, Requirements, CancellationToken.None))
            .ShouldBe(["Aviz medical"]);
    }

    [Fact]
    public async Task A_rejected_document_blocks_it()
    {
        using ApplicationDbContext db = await With(
            (DocumentCategory.CazierJudiciar, DocumentStatus.Rejected));

        (await DossierAttachments.UnverifiedAsync(db, Client, Requirements, CancellationToken.None))
            .ShouldBe(["Cazier judiciar"]);
    }

    /// <summary>
    /// Contează actul cel mai recent, cel care ar intra în dosar: o variantă nouă, încă nevăzută,
    /// blochează chiar dacă una veche fusese verificată.
    /// </summary>
    [Fact]
    public async Task A_newer_unverified_upload_blocks_even_over_an_older_verified_one()
    {
        using ApplicationDbContext db = await With(
            (DocumentCategory.CazierJudiciar, DocumentStatus.Verified, DateTime.UtcNow.AddDays(-2)),
            (DocumentCategory.CazierJudiciar, DocumentStatus.Pending, DateTime.UtcNow));

        (await DossierAttachments.UnverifiedAsync(db, Client, Requirements, CancellationToken.None))
            .ShouldBe(["Cazier judiciar"]);
    }

    [Fact]
    public void The_refusal_tells_the_client_what_is_still_in_review()
    {
        Error error = DossierAttachments.NotYetVerified(["Aviz medical", "Cazier judiciar"]);

        error.Description.ShouldContain("Aviz medical");
        error.Description.ShouldContain("Cazier judiciar");
    }

    private static Task<ApplicationDbContext> With(params (DocumentCategory Category, DocumentStatus Status)[] documents) =>
        With(documents.Select(d => (d.Category, d.Status, DateTime.UtcNow)).ToArray());

    private static async Task<ApplicationDbContext> With(
        params (DocumentCategory Category, DocumentStatus Status, DateTime UploadedAtUtc)[] documents)
    {
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new Events());

        foreach ((DocumentCategory category, DocumentStatus status, DateTime uploadedAtUtc) in documents)
        {
            db.Documents.Add(new Document
            {
                Id = Guid.NewGuid(),
                UserId = Client,
                Category = category,
                Status = status,
                UploadedAtUtc = uploadedAtUtc,
            });
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return db;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
