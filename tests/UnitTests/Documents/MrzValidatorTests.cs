using Domain.Documents;
using Shouldly;
using Xunit;

namespace UnitTests.Documents;

/// <summary>
/// Zona MRZ a actelor de identitate. Exemplele sunt cele din ICAO 9303 (Anna Maria Eriksson,
/// statul fictiv UTO), cu cifre de control corecte — orice caracter schimbat le strică.
/// </summary>
public sealed class MrzValidatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private const string Td1 =
        "I<UTOD231458907<<<<<<<<<<<<<<<\n" +
        "7408122F1204159UTO<<<<<<<<<<<6\n" +
        "ERIKSSON<<ANNA<MARIA<<<<<<<<<<";

    private const string Td2 =
        "I<UTOERIKSSON<<ANNA<MARIA<<<<<<<<<<<\n" +
        "D231458907UTO7408122F1204159<<<<<<<6";

    [Fact]
    public void A_valid_electronic_card_mrz_is_read()
    {
        MrzReading? mrz = MrzValidator.Parse(Td1, Today);

        mrz.ShouldNotBeNull();
        mrz.Format.ShouldBe("TD1");
        mrz.ChecksValid.ShouldBeTrue();
        mrz.DocumentNumber.ShouldBe("D23145890");
        mrz.Surname.ShouldBe("ERIKSSON");
        mrz.GivenNames.ShouldBe("ANNA MARIA");
        mrz.BirthDate.ShouldBe(new DateOnly(1974, 8, 12));
        mrz.ExpiryDate.ShouldBe(new DateOnly(2012, 4, 15));
    }

    [Fact]
    public void A_valid_classic_card_mrz_is_read()
    {
        MrzReading? mrz = MrzValidator.Parse(Td2, Today);

        mrz.ShouldNotBeNull();
        mrz.Format.ShouldBe("TD2");
        mrz.ChecksValid.ShouldBeTrue();
        mrz.DocumentNumber.ShouldBe("D23145890");
        mrz.BirthDate.ShouldBe(new DateOnly(1974, 8, 12));
    }

    /// <summary>Cine schimbă data nașterii pe act nu recalculează și cifra de control.</summary>
    [Fact]
    public void An_edited_birth_date_breaks_the_check() =>
        MrzValidator.Parse(Td1.Replace("7408122F", "7508122F", StringComparison.Ordinal), Today)!
            .ChecksValid.ShouldBeFalse();

    [Fact]
    public void An_edited_document_number_breaks_the_check() =>
        MrzValidator.Parse(Td2.Replace("D23145890", "D23145891", StringComparison.Ordinal), Today)!
            .ChecksValid.ShouldBeFalse();

    /// <summary>Transcrierea modelului vine cu spații, ghilimele unghiulare sau „|” între rânduri.</summary>
    [Fact]
    public void Transcription_noise_is_tolerated()
    {
        string noisy = Td2.Replace("\n", " | ", StringComparison.Ordinal).Replace("<<<", "«<<", StringComparison.Ordinal);

        MrzValidator.Parse(noisy, Today)!.ChecksValid.ShouldBeTrue();
    }

    [Fact]
    public void A_truncated_mrz_is_not_read()
    {
        MrzValidator.Parse("I<UTOD231458907<<<<<<<<<<<<<<<\n7408122F1204159", Today).ShouldBeNull();
        MrzValidator.Parse(null, Today).ShouldBeNull();
    }

    [Fact]
    public void The_check_digit_follows_icao() =>
        MrzValidator.CheckDigit("D23145890").ShouldBe(7);
}
