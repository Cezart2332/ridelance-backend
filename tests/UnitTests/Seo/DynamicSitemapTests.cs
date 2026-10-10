using Application.Seo;
using Domain.Cars;
using Domain.Companies;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Seo;

/// <summary>Sitemap-ul listează doar ce se vede public: anunțurile publicate și firmele care au unul.</summary>
public sealed class DynamicSitemapTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task OnlyPublicCars_AndCompaniesThatHaveOne_AreListed()
    {
        var withPublicCar = Guid.NewGuid();
        var withDraftOnly = Guid.NewGuid();
        _db.CompanyProfiles.AddRange(
            new CompanyProfile { Id = Guid.NewGuid(), UserId = withPublicCar, Slug = "tuki-go" },
            new CompanyProfile { Id = Guid.NewGuid(), UserId = withDraftOnly, Slug = "fara-anunturi" });
        _db.Cars.AddRange(
            Car("dacia-logan-2021", ListingStatus.Published, CarApprovalStatus.Approved, withPublicCar),
            Car("tesla-model-3-2022", ListingStatus.Published, CarApprovalStatus.Approved, owner: null),
            Car("in-asteptare", ListingStatus.Published, CarApprovalStatus.Pending, withPublicCar),
            Car("ciorna", ListingStatus.Draft, CarApprovalStatus.Approved, withDraftOnly));
        await _db.SaveChangesAsync();

        Result<string> result = await new GetDynamicSitemapQueryHandler(_db)
            .Handle(new GetDynamicSitemapQuery("https://ridelance.ro/"), default);

        result.IsSuccess.ShouldBeTrue();
        string xml = result.Value;
        xml.ShouldStartWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        Locations(xml).ShouldBe(
        [
            "https://ridelance.ro/masini/dacia-logan-2021",
            "https://ridelance.ro/masini/tesla-model-3-2022",
            "https://ridelance.ro/tuki-go",
        ]);
    }

    [Fact]
    public async Task EmptyMarketplace_GivesAValidEmptySitemap()
    {
        Result<string> result = await new GetDynamicSitemapQueryHandler(_db)
            .Handle(new GetDynamicSitemapQuery("https://ridelance.ro"), default);

        result.Value.ShouldContain("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        Locations(result.Value).ShouldBeEmpty();
    }

    private static Car Car(string slug, ListingStatus listing, CarApprovalStatus approval, Guid? owner) => new()
    {
        Id = Guid.NewGuid(),
        Brand = "Dacia",
        Model = "Logan",
        Slug = slug,
        ListingStatus = listing,
        ApprovalStatus = approval,
        PostedByUserId = owner,
    };

    private static List<string> Locations(string xml) =>
        [.. System.Xml.Linq.XDocument.Parse(xml).Descendants().Where(e => e.Name.LocalName == "loc").Select(e => e.Value)];

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
