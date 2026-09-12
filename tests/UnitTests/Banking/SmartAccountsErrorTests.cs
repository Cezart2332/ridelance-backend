using Infrastructure.Banking;
using Shouldly;
using Xunit;

namespace UnitTests.Banking;

/// <summary>
/// Ce scrie în eroare când Smart Accounts refuză o cerere.
///
/// Motivul e concret: „Cererea către Smart Accounts a eșuat (400): Bad request" a fost tot ce am
/// avut la un blocaj în producție. Eticheta aia e aceeași pentru orice greșeală — lipsa adresei de
/// retur, un IBAN greșit, un IP în format nepotrivit. Cauza stă în `moreDetails`, care e un obiect
/// cu un `text` înăuntru, deci nici nu putea fi citit ca șir.
///
/// Payload-urile de mai jos sunt copiate din răspunsurile reale ale sandbox-ului.
/// </summary>
public sealed class SmartAccountsErrorTests
{
    [Fact]
    public void The_reason_comes_out_not_just_the_label() =>
        SmartAccountsBankDataProvider.ExtractErrorMessage(
            """
            {"status":400,"messageStatus":"Bad request","source":"SmartAccounts",
             "moreDetails":{"text":"EMPTY OR MISSING REDIRECTURL"}}
            """)
            .ShouldContain("EMPTY OR MISSING REDIRECTURL");

    [Fact]
    public void The_label_is_kept_alongside_the_reason()
    {
        string message = SmartAccountsBankDataProvider.ExtractErrorMessage(
            """
            {"status":400,"messageStatus":"Bad request",
             "moreDetails":{"text":"INVALID FORMAT PSU-IP-ADDRESS"}}
            """);

        message.ShouldContain("Bad request");
        message.ShouldContain("INVALID FORMAT PSU-IP-ADDRESS");
    }

    /// <summary>Unele bănci împachetează încă un JSON înăuntru; iese ca text, nu se pierde.</summary>
    [Fact]
    public void A_reason_wrapped_as_json_still_comes_out() =>
        SmartAccountsBankDataProvider.ExtractErrorMessage(
            """
            {"status":400,"messageStatus":"Bad request",
             "moreDetails":{"text":"{\"TYPE\":\"TPPMESSAGE400AIS\",\"TITLE\":\"INVALID 'PSU-ID'\"}"}}
            """)
            .ShouldContain("INVALID 'PSU-ID'");

    [Fact]
    public void Without_details_the_label_is_still_reported() =>
        SmartAccountsBankDataProvider
            .ExtractErrorMessage("""{"status":500,"messageStatus":"Internal error"}""")
            .ShouldBe("Internal error");

    [Fact]
    public void An_empty_body_says_so_instead_of_throwing() =>
        SmartAccountsBankDataProvider.ExtractErrorMessage(string.Empty).ShouldBe("fără detalii");

    [Fact]
    public void Something_that_is_not_json_is_passed_through() =>
        SmartAccountsBankDataProvider
            .ExtractErrorMessage("<html>502 Bad Gateway</html>")
            .ShouldContain("502");
}
