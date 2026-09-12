using Application.Banking.Commands;
using Shouldly;
using Xunit;

namespace UnitTests.Banking;

/// <summary>
/// Ce ajunge în coloana de logo a conexiunii bancare.
///
/// Regresia, căzută în producție: Smart Accounts nu întoarce o adresă, ci imaginea însăși, ca
/// <c>data:image/svg;base64,…</c>. Măsurat pe sandbox, toate cele 15 bănci depășesc 512 caractere
/// — cea mai mică e Revolut, cu 1,2 KB; ING are 190 KB. Scris în coloana de 512, Postgres refuza
/// rândul întreg cu „22001: value too long", iar conectarea pica la salvare, după ce
/// consimțământul fusese deja deschis la furnizor.
/// </summary>
public sealed class BankConnectionLogoTests
{
    [Fact]
    public void A_data_uri_is_not_stored() =>
        InitiateBankConnectionCommandHandler
            .StorableLogo("data:image/svg;base64," + new string('A', 28_000))
            .ShouldBeNull();

    /// <summary>Nici măcar unul scurt: coloana e pentru adrese, nu pentru imagini.</summary>
    [Fact]
    public void A_short_data_uri_is_not_stored_either() =>
        InitiateBankConnectionCommandHandler.StorableLogo("data:image/png;base64,AAAA").ShouldBeNull();

    [Fact]
    public void A_real_address_is_kept() =>
        InitiateBankConnectionCommandHandler
            .StorableLogo("https://cdn.smartfintech.eu/logo/bt.svg")
            .ShouldBe("https://cdn.smartfintech.eu/logo/bt.svg");

    /// <summary>O adresă peste lungimea coloanei se lasă, nu se taie: un link rupt nu e un link.</summary>
    [Fact]
    public void An_address_longer_than_the_column_is_dropped() =>
        InitiateBankConnectionCommandHandler
            .StorableLogo("https://cdn.smartfintech.eu/" + new string('a', 600))
            .ShouldBeNull();

    [Fact]
    public void Nothing_stays_nothing() =>
        InitiateBankConnectionCommandHandler.StorableLogo(null).ShouldBeNull();
}
