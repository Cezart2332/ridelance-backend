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

/// <summary>Serverul site-ului află de aici dacă o adresă există și ce titlu are.</summary>
public sealed class PageHeadTests : IDisposable
{
    private const string Site = "https://ridelance.ro";

    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly Guid _owner = Guid.NewGuid();

    public PageHeadTests()
    {
        _db.CompanyProfiles.Add(new CompanyProfile { Id = Guid.NewGuid(), UserId = _owner, Slug = "tuki-go", LegalName = "TUKI GO SRL" });
        var car = new Car
        {
            Id = Guid.NewGuid(),
            Brand = "Dacia",
            Model = "Logan",
            Year = 2021,
            Slug = "dacia-logan-2021",
            Location = "București",
            Transmission = "Manuală",
            Engine = "GPL",
            PricePerWeek = 1200.5m,
            ListingStatus = ListingStatus.Published,
            ApprovalStatus = CarApprovalStatus.Approved,
            PostedByUserId = _owner,
        };
        car.Images.Add(new CarImage { Id = Guid.NewGuid(), CarId = car.Id, Url = "/uploads/cars/a-doua.jpg", FileName = "a-doua.jpg", DisplayOrder = 1 });
        car.Images.Add(new CarImage { Id = Guid.NewGuid(), CarId = car.Id, Url = "/uploads/cars/prima.jpg", FileName = "prima.jpg", DisplayOrder = 0 });
        _db.Cars.Add(car);
        _db.Cars.Add(new Car
        {
            Id = Guid.NewGuid(),
            Brand = "Tesla",
            Model = "Model 3",
            Slug = "ciorna",
            ListingStatus = ListingStatus.Draft,
            ApprovalStatus = CarApprovalStatus.Approved,
            PostedByUserId = _owner,
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Task<Result<PageHead>> Head(string path) =>
        new GetPageHeadQueryHandler(_db).Handle(new GetPageHeadQuery(path, Site + "/"), default);

    [Fact]
    public async Task PublicCar_HasTheTitleTheAppShows_AndItsFirstPhoto()
    {
        string html = (await Head("/masini/dacia-logan-2021")).Value.Html;

        html.ShouldContain("<title>Dacia Logan 2021 de închiriat în București — 1.201 lei/săptămână • RIDElance</title>");
        html.ShouldContain(
            "<meta name=\"description\" content=\"Dacia Logan 2021, manuală, gpl, disponibilă în București pentru ridesharing. " +
            "1.201 lei pe săptămână, fără plată online.\" />");
        html.ShouldContain("<link rel=\"canonical\" href=\"https://ridelance.ro/masini/dacia-logan-2021\" data-seo=\"\" />");
        html.ShouldContain("<meta property=\"og:image\" content=\"https://ridelance.ro/uploads/cars/prima.jpg\" data-seo=\"\" />");
        html.ShouldContain("<meta property=\"og:type\" content=\"product\" data-seo=\"\" />");
    }

    [Fact]
    public async Task CarOpenedFromItsCompany_KeepsTheMarketplaceAddressAsCanonical()
    {
        (await Head("/tuki-go/dacia-logan-2021")).Value.Html
            .ShouldContain("<link rel=\"canonical\" href=\"https://ridelance.ro/masini/dacia-logan-2021\" data-seo=\"\" />");

        // Aceeași mașină sub altă firmă nu e o pagină.
        _db.CompanyProfiles.Add(new CompanyProfile { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Slug = "alta-firma", LegalName = "Alta SRL" });
        await _db.SaveChangesAsync();
        (await Head("/alta-firma/dacia-logan-2021")).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/tuki-go")]
    [InlineData("/f/tuki-go")]
    [InlineData("/tuki-go/")]
    public async Task Company_IsOnePage_WhicheverAddressOpensIt(string path)
    {
        string html = (await Head(path)).Value.Html;

        html.ShouldContain("<title>TUKI GO SRL • RIDElance</title>");
        html.ShouldContain("<link rel=\"canonical\" href=\"https://ridelance.ro/tuki-go\" data-seo=\"\" />");
        html.ShouldContain("content=\"TUKI GO SRL — mașini de închiriat pentru ridesharing, pe RIDElance.\"");
    }

    [Theory]
    [InlineData("/masini/ciorna")]
    [InlineData("/masini/nu-exista")]
    [InlineData("/firma-care-nu-exista")]
    [InlineData("/tuki-go/nu-exista")]
    [InlineData("/tuki-go/dacia-logan-2021/altceva")]
    [InlineData("/wp-login.php")]
    [InlineData("/Tuki-Go")]
    public async Task NothingAtThatAddress_IsNotFound(string path)
    {
        Result<PageHead> result = await Head(path);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/masini")]
    [InlineData("/autentificare")]
    [InlineData("/app/dashboard/facturi")]
    [InlineData("/semneaza/Un.Token-cu_Semne")]
    [InlineData("/parteneri/un-partener")]
    public async Task AppPages_Exist_ButHaveNoTagsHere(string path)
    {
        Result<PageHead> result = await Head(path);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(PageHead.None);
    }

    [Fact]
    public void CompanyDescription_PrefersTheTagline_ThenTheStartOfTheAbout()
    {
        GetPageHeadQueryHandler.CompanyDescription("X SRL", "  Flotă  electrică \n în București ", "Despre noi")
            .ShouldBe("Flotă electrică în București");

        string about = string.Join(' ', Enumerable.Repeat("cuvânt", 60));
        string cut = GetPageHeadQueryHandler.CompanyDescription("X SRL", null, about);
        cut.Length.ShouldBeLessThanOrEqualTo(160);
        cut.ShouldEndWith("…");
        cut.ShouldStartWith("cuvânt cuvânt");
    }

    [Fact]
    public async Task TextFromTheDatabase_IsEscaped()
    {
        CompanyProfile profile = await _db.CompanyProfiles.SingleAsync(p => p.Slug == "tuki-go");
        profile.LegalName = "A & B \"Rent\" <SRL>";
        await _db.SaveChangesAsync();

        string html = (await Head("/tuki-go")).Value.Html;

        html.ShouldContain("<title>A &amp; B &quot;Rent&quot; &lt;SRL&gt; • RIDElance</title>");
        html.ShouldNotContain("<SRL>");
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
