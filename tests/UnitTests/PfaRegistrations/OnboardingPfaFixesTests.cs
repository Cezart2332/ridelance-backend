using Application.PfaRegistrations.Onboarding.Platforms;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.CompanyFormation;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>
/// Regulile din specul de fix-uri „Onboarding PFA + Dashboard PFA". Fiecare test ține pe loc un
/// criteriu de acceptare — dacă unul pică, un fix s-a pierdut la o refactorizare.
/// </summary>
public sealed class OnboardingPfaFixesTests
{
    /* P0-2 — plata e condiție de trimitere, nu un pas de după ea. */

    [Fact]
    public void SignedButUnpaidDossier_CannotBeSentToConsulto()
    {
        var request = new CompanyFormationRequest
        {
            Id = Guid.NewGuid(),
            Status = CompanyFormationStatus.AwaitingPayment,
        };

        request.CanSendToConsulto.ShouldBeFalse();
    }

    [Fact]
    public void PaidDossier_CanBeSentOnce()
    {
        var request = new CompanyFormationRequest
        {
            Id = Guid.NewGuid(),
            Status = CompanyFormationStatus.PaymentConfirmed,
            PaymentConfirmedAtUtc = DateTime.UtcNow,
        };

        request.CanSendToConsulto.ShouldBeTrue();

        // A doua oară nu: stampila de trimitere e cheia de dedupe pentru retry-urile Stripe.
        request.SentToConsultoAtUtc = DateTime.UtcNow;
        request.CanSendToConsulto.ShouldBeFalse();
    }

    [Theory]
    [InlineData(CompanyFormationStatus.Draft)]
    [InlineData(CompanyFormationStatus.Submitted)]
    [InlineData(CompanyFormationStatus.AwaitingPayment)]
    [InlineData(CompanyFormationStatus.InfoRequested)]
    public void WithoutPaymentConfirmation_NoStatusOpensTheGate(CompanyFormationStatus status)
    {
        var request = new CompanyFormationRequest { Id = Guid.NewGuid(), Status = status };

        request.CanSendToConsulto.ShouldBeFalse();
    }

    [Fact]
    public void AwaitingPaymentDossier_IsLockedForEditing()
    {
        var request = new CompanyFormationRequest
        {
            Id = Guid.NewGuid(),
            Status = CompanyFormationStatus.AwaitingPayment,
        };

        // Semnat înseamnă blocat, chiar dacă nu a plecat nicăieri.
        request.IsLocked.ShouldBeTrue();
    }

    /* P0-3 — pasul nu e complet fără conturile de șofer. */

    [Fact]
    public void PlatformStep_IsIncompleteWithOnlyTheFleetAccount()
    {
        PfaPlatformAccount account = FleetOnly();

        PlatformShared.UserPartComplete(account).ShouldBeFalse();
    }

    [Fact]
    public void PlatformStep_IsCompleteWithBothAccounts()
    {
        PfaPlatformAccount account = FleetOnly();
        account.DriverEmail = "sofer@example.com";
        account.DriverPhone = "+40712345678";

        PlatformShared.UserPartComplete(account).ShouldBeTrue();
    }

    [Fact]
    public void DriverExternalId_StaysOptional()
    {
        PfaPlatformAccount account = FleetOnly();
        account.DriverEmail = "sofer@example.com";
        account.DriverPhone = "+40712345678";
        account.DriverExternalId = null;

        PlatformShared.UserPartComplete(account).ShouldBeTrue();
    }

    /* P0-3 — telefonul se normalizează la E.164, nu doar se respinge. */

    [Theory]
    [InlineData("0712345678", "+40712345678")]
    [InlineData("0040712345678", "+40712345678")]
    [InlineData("+40712345678", "+40712345678")]
    [InlineData("+40 712 345 678", "+40712345678")]
    [InlineData("0712-345.678", "+40712345678")]
    public void Phone_NormalisesToE164(string typed, string expected) =>
        PlatformContactRules.ToE164(typed).ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("07123")]
    [InlineData("telefon")]
    [InlineData("+4071234567890123456")]
    public void Phone_RejectsWhatIsNotANumber(string typed) =>
        PlatformContactRules.ToE164(typed).ShouldBeNull();

    [Theory]
    [InlineData("sofer@example.com")]
    [InlineData("a.b+c@sub.example.co.uk")]
    public void Email_AcceptsPlausibleAddresses(string value) =>
        PlatformContactRules.IsValidEmail(value).ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("fara-arond")]
    [InlineData("a@b")]
    [InlineData("a b@example.com")]
    public void Email_RejectsMalformedAddresses(string value) =>
        PlatformContactRules.IsValidEmail(value).ShouldBeFalse();

    private static PfaPlatformAccount FleetOnly() => new()
    {
        Id = Guid.NewGuid(),
        Provider = PfaPlatformProvider.Uber,
        Kind = PfaPlatformAccountKind.Driver,
        Email = "flota@example.com",
        Phone = "+40712345678",
        PasswordProtected = "protected",
        ExistingAccountAnswer = "None",
    };
}
