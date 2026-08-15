using Application.Expenses.Ocr;
using Shouldly;
using Xunit;

namespace UnitTests.Expenses;

/// <summary>
/// Sumele citite de pe bonuri. Formatul nu e unul singur nici măcar în România, iar greșeala
/// costisitoare e să confunzi separatorul de mii cu cel zecimal: „1.234" citit ca 1,23 lei
/// scoate o mie de lei din cheltuielile deductibile.
/// </summary>
public sealed class MoneyParserTests
{
    [Theory]
    [InlineData("284,00", 284.00)]
    [InlineData("284.00", 284.00)]
    [InlineData("284", 284)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("123.456,78", 123456.78)]
    public void Reads_the_formats_that_appear_on_romanian_receipts(string raw, double expected)
    {
        MoneyParser.Parse(raw).ShouldBe((decimal)expected);
    }

    [Theory]
    [InlineData("284,00 LEI", 284.00)]
    [InlineData("RON 1.500,00", 1500.00)]
    [InlineData("  650,50  ", 650.50)]
    [InlineData("Total: 99,99 lei", 99.99)]
    public void Currency_words_and_spacing_are_stripped(string raw, double expected)
    {
        MoneyParser.Parse(raw).ShouldBe((decimal)expected);
    }

    [Fact]
    public void A_thousands_separator_is_not_mistaken_for_a_decimal_one()
    {
        // Cazul care costă bani: 1.234 sunt o mie două sute treizeci și patru de lei.
        MoneyParser.Parse("1.234").ShouldBe(1234m);
        MoneyParser.Parse("1,234").ShouldBe(1234m);
    }

    [Fact]
    public void Two_decimals_stay_decimals()
    {
        MoneyParser.Parse("12,50").ShouldBe(12.50m);
        MoneyParser.Parse("12.50").ShouldBe(12.50m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("total")]
    [InlineData("—")]
    public void Unreadable_input_becomes_null_not_a_guess(string? raw)
    {
        MoneyParser.Parse(raw).ShouldBeNull();
    }

    [Fact]
    public void An_implausibly_large_amount_is_refused()
    {
        // O citire stricată produce des cifre lipite; nu se salvează ca sumă reală.
        MoneyParser.Parse("99.999.999,00").ShouldBeNull();
    }

    [Fact]
    public void Negative_amounts_keep_their_sign()
    {
        // Storno pe bon.
        MoneyParser.Parse("-45,90").ShouldBe(-45.90m);
    }

    [Fact]
    public void More_than_two_decimals_are_rounded_away_from_zero()
    {
        MoneyParser.Parse("10.5678").ShouldBe(10.57m);
    }

    [Fact]
    public void Exactly_three_digits_after_a_separator_are_read_as_grouping()
    {
        // Cazul cu adevărat ambiguu: „10,005" poate fi zece mii cinci sau zece virgulă zero
        // zero cinci. Banii au două zecimale, nu trei, deci gruparea e citirea mai sigură —
        // aceeași regulă care face din „1.234" o mie două sute treizeci și patru.
        MoneyParser.Parse("10,005").ShouldBe(10005m);
    }

    [Fact]
    public void Vat_above_the_total_is_implausible()
    {
        MoneyParser.IsVatPlausible(total: 100m, vat: 19m).ShouldBeTrue();
        MoneyParser.IsVatPlausible(total: 100m, vat: 100m).ShouldBeTrue();
        MoneyParser.IsVatPlausible(total: 100m, vat: 101m).ShouldBeFalse();
        MoneyParser.IsVatPlausible(total: 100m, vat: -1m).ShouldBeFalse();
    }

    [Fact]
    public void A_missing_side_makes_the_vat_check_pass()
    {
        // Lipsa unei valori nu e o contradicție — formularul o cere de la om.
        MoneyParser.IsVatPlausible(total: null, vat: 19m).ShouldBeTrue();
        MoneyParser.IsVatPlausible(total: 100m, vat: null).ShouldBeTrue();
    }
}
