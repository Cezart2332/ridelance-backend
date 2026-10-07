using Application.Documents.AiVerification;
using Shouldly;
using Xunit;

namespace UnitTests.Documents;

/// <summary>
/// Documentele sunt ale aceleiași persoane ca buletinul. Înainte nimic nu verifica asta: permisul
/// altcuiva trecea, iar eligibilitatea se calcula din el.
/// </summary>
public sealed class IdentityCrossCheckTests
{
    private static readonly IdentityFacts IdCard = new(
        Surname: "POPESCU-IONESCU",
        GivenNames: "ANDREI MIHAI",
        Cnp: "1900101400019",
        BirthDate: new DateOnly(1990, 1, 1),
        DocumentNumber: "RX123456",
        ExpiresOn: new DateOnly(2030, 1, 1));

    private static IReadOnlyList<string> Check(IdentityFacts candidate, bool numbers = false) =>
        IdentityCrossCheck.Mismatches(IdCard, candidate, "Permis de conducere", numbers);

    [Fact]
    public void The_same_person_written_differently_matches() =>
        Check(new IdentityFacts(FullName: "Mihai Popescu Ionescu", Cnp: "1900101400019")).ShouldBeEmpty();

    [Fact]
    public void Diacritics_and_word_order_do_not_matter() =>
        IdentityCrossCheck.Mismatches(
                new IdentityFacts(Surname: "ȘTEFĂNESCU", GivenNames: "ȚICU"),
                new IdentityFacts(FullName: "Ticu Stefanescu"),
                "adeverință")
            .ShouldBeEmpty();

    /// <summary>Permisul are adesea un singur prenume din două.</summary>
    [Fact]
    public void One_of_two_given_names_is_enough() =>
        Check(new IdentityFacts(Surname: "Popescu-Ionescu", GivenNames: "Andrei")).ShouldBeEmpty();

    [Fact]
    public void Another_persons_name_is_a_mismatch()
    {
        IReadOnlyList<string> reasons = Check(new IdentityFacts(Surname: "Georgescu", GivenNames: "Vlad"));

        reasons.ShouldHaveSingleItem().ShouldContain("Numele");
        IdentityCrossCheck.IsIdentityReason(reasons[0]).ShouldBeTrue();
    }

    /// <summary>Același nume de familie, alt prenume: frate, părinte — altă persoană.</summary>
    [Fact]
    public void Same_surname_with_another_given_name_is_a_mismatch() =>
        Check(new IdentityFacts(Surname: "Popescu", GivenNames: "Elena")).ShouldHaveSingleItem();

    [Fact]
    public void A_different_cnp_is_a_mismatch() =>
        Check(new IdentityFacts(FullName: "Andrei Popescu", Cnp: "2900101400011"))
            .ShouldHaveSingleItem().ShouldContain("CNP");

    [Fact]
    public void A_different_birth_date_is_a_mismatch() =>
        Check(new IdentityFacts(FullName: "Andrei Popescu", BirthDate: new DateOnly(1991, 1, 1)))
            .ShouldHaveSingleItem().ShouldContain("nașterii");

    /// <summary>Ce lipsește de pe o parte nu se compară: lipsa nu e o nepotrivire.</summary>
    [Fact]
    public void Missing_values_are_not_mismatches()
    {
        Check(new IdentityFacts()).ShouldBeEmpty();
        IdentityCrossCheck.Mismatches(new IdentityFacts(), new IdentityFacts(FullName: "Oricine"), "permis").ShouldBeEmpty();
    }

    /// <summary>Poza cărții electronice față de PDF-ul ei: aceeași serie și aceeași expirare.</summary>
    [Fact]
    public void Two_identity_documents_compare_their_numbers()
    {
        var pdf = new IdentityFacts(Surname: "Popescu-Ionescu", GivenNames: "Andrei Mihai", DocumentNumber: "RX 999999");

        Check(pdf, numbers: true).ShouldHaveSingleItem().ShouldContain("Seria");
        Check(pdf, numbers: false).ShouldBeEmpty();
    }

    [Fact]
    public void Facts_are_read_from_the_catalog_keys()
    {
        var facts = IdentityFacts.From(new Dictionary<string, string>
        {
            ["titular_nume"] = "Popescu",
            ["titular_prenume"] = "Andrei",
            ["cnp_titular"] = "1900101400019",
            ["titular_data_nasterii"] = "1990-01-01",
        });

        facts.Surname.ShouldBe("Popescu");
        facts.GivenNames.ShouldBe("Andrei");
        facts.Cnp.ShouldBe("1900101400019");
        facts.BirthDate.ShouldBe(new DateOnly(1990, 1, 1));
    }
}
