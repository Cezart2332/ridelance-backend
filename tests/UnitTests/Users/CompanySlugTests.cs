using Domain.Companies;
using Shouldly;
using Xunit;

namespace UnitTests.Users;

/// <summary>
/// Mini-site-ul firmei stă la rădăcină, deci slug-ul unei firme e o cale a site-ului.
/// </summary>
public sealed class CompanySlugTests
{
    [Fact]
    public void Denumirea_devine_slug_citibil()
    {
        CompanySlug.Generate("Tuki Go S.R.L.").ShouldBe("tuki-go-s-r-l");
    }

    [Theory]
    [InlineData("Masini")]
    [InlineData("parteneri")]
    [InlineData("ADMIN")]
    [InlineData("abonamente-preturi")]
    public void Cuvintele_care_sunt_deja_pagini_nu_pot_fi_slug_de_firma(string slug)
    {
        CompanySlug.IsReserved(slug).ShouldBeTrue();
    }

    [Fact]
    public void O_denumire_obisnuita_nu_e_rezervata()
    {
        CompanySlug.IsReserved(CompanySlug.Generate("Tuki Go")).ShouldBeFalse();
    }

    [Fact]
    public void Dezambiguizarea_adauga_un_sufix_stabil()
    {
        var id = Guid.Parse("4f3a1b2c-0000-0000-0000-000000000000");

        CompanySlug.Disambiguate("parteneri", id).ShouldBe("parteneri-4f3a");
    }
}
