using Domain.Notifications;
using Domain.Taxes;
using Shouldly;
using Xunit;

namespace UnitTests.Taxes;

/// <summary>
/// „Termen depășit" e o afirmație pe care utilizatorul o citește ca pe o problemă deschisă,
/// deci trebuie să fie adevărată exact atunci și numai atunci.
/// </summary>
public sealed class TaxObligationTests
{
    private static readonly DateOnly Today = new(2026, 8, 25);

    private static TaxObligation Obligation(DateOnly dueDate, TaxObligationStatus status) =>
        new() { DueDate = dueDate, Status = status };

    [Fact]
    public void An_unpaid_obligation_past_its_due_date_is_overdue()
    {
        Obligation(new DateOnly(2026, 8, 24), TaxObligationStatus.DePlata).IsOverdue(Today).ShouldBeTrue();
    }

    [Fact]
    public void The_due_date_itself_is_not_yet_overdue()
    {
        // Ai toată ziua termenului ca să plătești.
        Obligation(Today, TaxObligationStatus.DePlata).IsOverdue(Today).ShouldBeFalse();
    }

    [Fact]
    public void A_paid_obligation_is_never_overdue()
    {
        // Plătită târziu nu mai e o problemă deschisă; a o marca roșu ar fi doar reproș.
        Obligation(new DateOnly(2026, 1, 1), TaxObligationStatus.Platita).IsOverdue(Today).ShouldBeFalse();
    }

    [Theory]
    [InlineData(TaxObligationStatus.InPregatire)]
    [InlineData(TaxObligationStatus.Depusa)]
    [InlineData(TaxObligationStatus.DePlata)]
    public void Every_unpaid_status_can_go_overdue(TaxObligationStatus status)
    {
        // Și o declarație „în pregătire" își depășește termenul — mai ales aceea.
        Obligation(new DateOnly(2026, 7, 25), status).IsOverdue(Today).ShouldBeTrue();
    }

    [Fact]
    public void A_future_deadline_is_not_overdue()
    {
        Obligation(new DateOnly(2026, 9, 25), TaxObligationStatus.DePlata).IsOverdue(Today).ShouldBeFalse();
    }
}

/// <summary>
/// Preferințele de notificări: ce se poate opri și ce nu. Un tip fără categorie e un anunț de
/// sistem — nu are comutator, deci nu poate fi tăiat din greșeală.
/// </summary>
public sealed class NotificationPreferenceTests
{
    [Fact]
    public void Operational_and_commercial_are_separated()
    {
        NotificationPreference.IsCommercial(NotificationCategory.DocumentExpiry).ShouldBeFalse();
        NotificationPreference.IsCommercial(NotificationCategory.TaxesAndDeadlines).ShouldBeFalse();
        NotificationPreference.IsCommercial(NotificationCategory.AccountantMessages).ShouldBeFalse();
        NotificationPreference.IsCommercial(NotificationCategory.PlatformSyncIssues).ShouldBeFalse();

        NotificationPreference.IsCommercial(NotificationCategory.RidelanceUpdates).ShouldBeTrue();
        NotificationPreference.IsCommercial(NotificationCategory.OffersAndBenefits).ShouldBeTrue();
    }

    [Fact]
    public void Document_expiry_notifications_map_to_their_category()
    {
        NotificationPreference.CategoryForType(NotificationTypes.DocumentExpiringSoon)
            .ShouldBe(NotificationCategory.DocumentExpiry);
    }

    [Fact]
    public void System_announcements_have_no_category_so_they_cannot_be_turned_off()
    {
        // Statusul PFA-ului sau un pas de onboarding nu sunt opțiuni de confort.
        NotificationPreference.CategoryForType(NotificationTypes.PfaStatusUpdate).ShouldBeNull();
        NotificationPreference.CategoryForType(NotificationTypes.OnboardingStepUpdate).ShouldBeNull();
    }
}
