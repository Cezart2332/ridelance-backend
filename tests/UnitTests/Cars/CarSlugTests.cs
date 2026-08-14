using Domain.Cars;
using Shouldly;
using Xunit;

namespace UnitTests.Cars;

/// <summary>
/// Slug-ul e identitatea publică a unui anunț și intră într-un index unic. Dacă generatorul lasă
/// să treacă un spațiu, un diacritic sau o cratimă dublă, se vede direct în URL — și, la prima
/// coliziune, într-o eroare de salvare.
/// </summary>
public class CarSlugTests
{
    private static readonly Guid Id = Guid.Parse("4f3ab2c1-0000-0000-0000-000000000000");

    [Fact]
    public void Generate_BuildsMakeModelYearWithSuffix()
    {
        CarSlug.Generate("Dacia", "Logan", 2022, Id).ShouldBe("dacia-logan-2022-4f3a");
    }

    [Fact]
    public void Generate_IsStableForTheSameCar()
    {
        CarSlug.Generate("Dacia", "Logan", 2022, Id)
            .ShouldBe(CarSlug.Generate("Dacia", "Logan", 2022, Id));
    }

    [Fact]
    public void Generate_SeparatesTwoListingsOfTheSameModel()
    {
        string first = CarSlug.Generate("Dacia", "Logan", 2022, Id);
        string second = CarSlug.Generate("Dacia", "Logan", 2022, Guid.Parse("99887766-0000-0000-0000-000000000000"));

        first.ShouldNotBe(second);
    }

    [Theory]
    [InlineData("Škoda", "Octavia", "skoda-octavia")]
    [InlineData("Citroën", "Ë-C4", "citroen-e-c4")]
    [InlineData("Dacia", "Logan Ștefan țară", "dacia-logan-stefan-tara")]
    public void Slugify_DropsDiacritics(string brand, string model, string expected)
    {
        CarSlug.Generate(brand, model, 2022, Id).ShouldBe($"{expected}-2022-4f3a");
    }

    [Fact]
    public void Slugify_CollapsesPunctuationIntoSingleHyphens()
    {
        CarSlug.Slugify("  Renault   Clio (facelift) / 2023!! ").ShouldBe("renault-clio-facelift-2023");
    }

    [Fact]
    public void Slugify_ReturnsEmptyForTextWithoutLetters()
    {
        CarSlug.Slugify("!!! ---").ShouldBe(string.Empty);
    }

    [Fact]
    public void Generate_FallsBackToTheSuffixWhenThereIsNoUsableText()
    {
        // Nu poate rămâne fără slug: coloana e obligatorie și unică.
        CarSlug.Generate("", "", 0, Id).ShouldBe("0-4f3a");
    }

    [Fact]
    public void Generate_StaysInsideTheColumn()
    {
        string slug = CarSlug.Generate(new string('a', 200), new string('b', 200), 2022, Id);

        slug.Length.ShouldBeLessThanOrEqualTo(160);
    }
}
