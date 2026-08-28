using Application.Cars;
using Shouldly;
using Xunit;

namespace UnitTests.Cars;

/// <summary>
/// Sursa de trafic vine dintr-un parametru de URL, deci vine de la oricine.
/// </summary>
/// <remarks>
/// `utm_source` e scris de cel care face linkul, nu de noi. Ajunge în două tabele și, de acolo,
/// afișat în dashboardul flotei — adică exact traseul pe care un șir de 4000 de caractere sau o
/// bucată de marcaj n-are voie să-l parcurgă neatins.
/// </remarks>
public sealed class TrafficSourceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<>")]
    public void Fara_sursa_utila_ramane_vizita_directa(string? raw)
    {
        TrafficSource.Normalize(raw).ShouldBe(TrafficSource.Direct);
    }

    [Theory]
    [InlineData("facebook", "facebook")]
    [InlineData("  google-ads  ", "google-ads")]
    [InlineData("email_campanie.1", "email_campanie.1")]
    public void Sursele_obisnuite_trec_neschimbate(string raw, string expected)
    {
        TrafficSource.Normalize(raw).ShouldBe(expected);
    }

    [Fact]
    public void Caracterele_care_n_au_ce_cauta_intr_o_sursa_cad()
    {
        TrafficSource.Normalize("<script>fb</script>").ShouldBe("scriptfbscript");
    }

    [Fact]
    public void Sursa_prea_lunga_se_taie_la_lungimea_coloanei()
    {
        string result = TrafficSource.Normalize(new string('a', 500));

        result.Length.ShouldBe(TrafficSource.MaxLength);
    }
}
