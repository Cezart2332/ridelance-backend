using Application.Companies.Commands.GenerateCompanyDescription;
using Domain.Cars;
using Domain.Companies;
using Shouldly;
using Xunit;

namespace UnitTests.Companies;

/// <summary>
/// Ce fapte ajung la model când proprietarul apasă „Scrie cu AI".
///
/// Contează pentru că textul iese pe o pagină publică: modelul poate scrie doar despre ce i-am
/// pus în față. Ce nu e aici n-are cum să apară în descriere decât inventat.
/// </summary>
public sealed class CompanyDescriptionPromptTests
{
    private static CompanyProfile Profile() => new()
    {
        LegalName = "Tuki Go SRL",
        OwnerType = OwnerType.Srl,
        RegisteredOffice = "Str. Lungă 4, București",
    };

    private static Car Car(string brand, decimal price, int year = 2021) => new()
    {
        Brand = brand,
        Model = "Model",
        Year = year,
        PricePerWeek = price,
    };

    [Fact]
    public void Promptul_Contine_Datele_Reale_Ale_Firmei()
    {
        string prompt = GenerateCompanyDescriptionCommandHandler.BuildUserPrompt(
            Profile(),
            [Car("Dacia", 800), Car("Toyota", 1200)],
            hints: string.Empty);

        prompt.ShouldContain("Tuki Go SRL");
        prompt.ShouldContain("București");
        prompt.ShouldContain("Mașini publicate acum: 2");
        prompt.ShouldContain("Dacia");
        prompt.ShouldContain("între 800 și 1200");
    }

    [Fact]
    public void Fara_Masini_Nu_Se_Inventeaza_Preturi()
    {
        string prompt = GenerateCompanyDescriptionCommandHandler.BuildUserPrompt(
            Profile(), [], hints: string.Empty);

        prompt.ShouldContain("Mașini publicate acum: 0");
        prompt.ShouldNotContain("lei/săptămână");
        prompt.ShouldContain("nu a adăugat indicații");
    }

    [Fact]
    public void Un_Singur_Pret_Se_Scrie_Ca_Pret_Nu_Ca_Interval()
    {
        string prompt = GenerateCompanyDescriptionCommandHandler.BuildUserPrompt(
            Profile(), [Car("Dacia", 900), Car("Dacia", 900)], hints: string.Empty);

        prompt.ShouldContain("Preț: 900 lei/săptămână");
        prompt.ShouldNotContain("între");
    }

    [Fact]
    public void Verificarea_Apare_Doar_Cand_Exista()
    {
        CompanyProfile plain = Profile();
        CompanyProfile verified = Profile();
        verified.IsVerified = true;

        GenerateCompanyDescriptionCommandHandler.BuildUserPrompt(plain, [], string.Empty)
            .ShouldNotContain("verificată");
        GenerateCompanyDescriptionCommandHandler.BuildUserPrompt(verified, [], string.Empty)
            .ShouldContain("verificată");
    }

    [Fact]
    public void Indicatiile_Proprietarului_Ajung_Separate_De_Fapte()
    {
        string prompt = GenerateCompanyDescriptionCommandHandler.BuildUserPrompt(
            Profile(), [], hints: "predăm în 24 de ore, fără avans");

        prompt.ShouldContain("în cuvintele lui");
        prompt.ShouldContain("predăm în 24 de ore");
    }
}
