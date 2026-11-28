using Application.Admin;
using Domain.Payments;
using Shouldly;
using Xunit;

namespace UnitTests.Admin;

/// <summary>
/// Eticheta abonamentului din listele admin. Varianta cu abonamentul se chema pe ea însăși în loc de
/// cea cu statusul: stack overflow la primul client fără reîncercări, care oprea tot API-ul.
/// </summary>
public sealed class SubscriptionStatusLabelTests
{
    [Theory]
    [InlineData(SubscriptionStatus.Active, "Activ")]
    [InlineData(SubscriptionStatus.PastDue, "Plată eșuată")]
    [InlineData(SubscriptionStatus.Expired, "Suspendat")]
    public void A_subscription_without_retries_shows_its_status(SubscriptionStatus status, string label)
    {
        GetAdminOverviewQueryHandler.SubscriptionStatusLabel(new UserSubscription { Status = status }).ShouldBe(label);
    }

    [Fact]
    public void No_subscription_is_no_subscription()
    {
        GetAdminOverviewQueryHandler.SubscriptionStatusLabel((UserSubscription?)null).ShouldBe("Fără abonament");
    }

    [Fact]
    public void A_retrying_subscription_shows_the_attempt()
    {
        var subscription = new UserSubscription
        {
            Status = SubscriptionStatus.Active,
            PaymentRetryInvoiceId = "in_1",
            NextPaymentRetryAtUtc = new DateTime(2026, 11, 30, 14, 30, 0, DateTimeKind.Utc),
            PaymentRetryAttempts = 1,
        };

        GetAdminOverviewQueryHandler.SubscriptionStatusLabel(subscription).ShouldBe("Plată eșuată · reîncercare 2 din 5");
    }
}
