using Application.Companies.Page;
using Domain.Companies;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Companies;

/// <summary>
/// Ce trece și ce nu trece de la editorul de mini-site către baza de date.
///
/// Personalizarea ajunge pe o pagină publică, deci verificările astea sunt ultima poartă înainte
/// ca textul cuiva să fie servit oricui deschide linkul.
/// </summary>
public sealed class CompanyPageSanitizerTests
{
    private static CompanyPageTheme ValidTheme() => new()
    {
        Accent = "#123456",
        Background = "#ffffff",
        Surface = "#F0F0F0",
        Text = "#000000",
        ButtonText = "#FFFFFF",
        HeroOverlay = "#0B1220",
        HeroOverlayOpacity = 40,
    };

    [Fact]
    public void Theme_Fara_Nimic_Salvat_Da_Implicitele()
    {
        CompanyPageTheme theme = CompanyPageSanitizer.SanitizeTheme(null).Value;

        theme.Accent.ShouldBe("#5CCBF5");
        theme.HeroOverlayOpacity.ShouldBe(55);
    }

    [Fact]
    public void Theme_Normalizeaza_Hexul_La_Majuscule()
    {
        CompanyPageTheme theme = CompanyPageSanitizer.SanitizeTheme(ValidTheme()).Value;

        theme.Background.ShouldBe("#FFFFFF");
        theme.Accent.ShouldBe("#123456");
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("#12345")]
    [InlineData("#12345G")]
    [InlineData("red")]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    public void Theme_Refuza_Ce_Nu_E_Hex(string value)
    {
        CompanyPageTheme theme = ValidTheme();
        theme.Accent = value;

        Result<CompanyPageTheme> result = CompanyPageSanitizer.SanitizeTheme(theme);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyPage.InvalidColor");
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(200, 90)]
    [InlineData(30, 30)]
    public void Opacitatea_Se_Limiteaza_Nu_Se_Refuza(int given, int expected)
    {
        CompanyPageTheme theme = ValidTheme();
        theme.HeroOverlayOpacity = given;

        CompanyPageSanitizer.SanitizeTheme(theme).Value.HeroOverlayOpacity.ShouldBe(expected);
    }

    [Fact]
    public void Randurile_Goale_Dispar_Tacut()
    {
        var content = new CompanyPageContent
        {
            Highlights =
            [
                new CompanyPageHighlight { IconKey = "shield", Title = "  ", Text = "   " },
                new CompanyPageHighlight { IconKey = "clock", Title = " Predare rapidă ", Text = "În 24 de ore." },
            ],
            Faq =
            [
                // O întrebare fără răspuns arată ca o secțiune ruptă, deci nu se salvează.
                new CompanyPageFaq { Question = "Cât costă?", Answer = "  " },
                new CompanyPageFaq { Question = "Ce acte îmi trebuie?", Answer = "Buletin și permis." },
            ],
            CoverageAreas = ["București", "  ", "Ilfov"],
        };

        CompanyPageContent clean = CompanyPageSanitizer.SanitizeContent(content).Value;

        clean.Highlights.Count.ShouldBe(1);
        clean.Highlights[0].Title.ShouldBe("Predare rapidă");
        clean.Faq.Count.ShouldBe(1);
        clean.CoverageAreas.ShouldBe(["București", "Ilfov"]);
    }

    [Fact]
    public void Iconita_Necunoscuta_Cade_Pe_Bifa_Fara_Sa_Piarda_Textul()
    {
        var content = new CompanyPageContent
        {
            Highlights = [new CompanyPageHighlight { IconKey = "<script>", Title = "Asigurare", Text = "Inclusă." }],
        };

        CompanyPageContent clean = CompanyPageSanitizer.SanitizeContent(content).Value;

        clean.Highlights[0].IconKey.ShouldBe("check");
        clean.Highlights[0].Title.ShouldBe("Asigurare");
    }

    [Fact]
    public void Peste_Plafon_Se_Refuza_Cu_Mesaj()
    {
        var content = new CompanyPageContent
        {
            Highlights = Enumerable.Range(0, CompanyPageSanitizer.MaxHighlights + 1)
                .Select(i => new CompanyPageHighlight { Title = $"Avantaj {i}", Text = "Text" })
                .ToList(),
        };

        Result<CompanyPageContent> result = CompanyPageSanitizer.SanitizeContent(content);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyPage.TooManyItems");
    }

    [Fact]
    public void Caracterele_De_Control_Se_Scot_Iar_Randurile_Noi_Raman_Unde_Au_Sens()
    {
        var content = new CompanyPageContent
        {
            Highlights = [new CompanyPageHighlight { Title = "Titlu\nrupt", Text = "Prima linie\nA doua " }],
        };

        CompanyPageContent clean = CompanyPageSanitizer.SanitizeContent(content).Value;

        clean.Highlights[0].Title.ShouldBe("Titlurupt");
        clean.Highlights[0].Text.ShouldBe("Prima linie\nA doua");
    }

    [Fact]
    public void Textul_Prea_Lung_Se_Taie_La_Plafon()
    {
        string tagline = new('a', CompanyPageSanitizer.MaxTagline + 50);

        CompanyPageSanitizer.CleanText(tagline, CompanyPageSanitizer.MaxTagline)!
            .Length.ShouldBe(CompanyPageSanitizer.MaxTagline);
    }

    [Fact]
    public void Textul_Gol_Devine_Null_Nu_Sir_Gol()
    {
        // „Necompletat" și „completat cu nimic" trebuie să fie aceeași stare în baza de date.
        CompanyPageSanitizer.CleanText("   ", 100).ShouldBeNull();
    }
}
