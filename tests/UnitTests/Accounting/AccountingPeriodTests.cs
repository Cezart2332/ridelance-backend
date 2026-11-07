using System.Globalization;
using Application.Accounting;
using Application.Notifications.RecurringDocumentation;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>
/// Luna contabilă: bonurile pe septembrie se primesc până pe 5 octombrie; un document încărcat de
/// pe 6 septembrie până pe 5 octombrie se socotește la septembrie.
/// </summary>
public sealed class AccountingPeriodTests
{
    [Theory]
    [InlineData("2026-09-05", 2026, 8)]   // ultima zi pentru august
    [InlineData("2026-09-06", 2026, 9)]   // se deschide septembrie
    [InlineData("2026-10-01", 2026, 9)]   // anunțul de închidere a lui septembrie
    [InlineData("2026-10-05", 2026, 9)]   // ultima zi pentru septembrie
    [InlineData("2026-10-06", 2026, 10)]
    [InlineData("2026-01-03", 2025, 12)]  // peste an
    [InlineData("2026-12-31", 2026, 12)]
    public void RequestedMonth_FollowsThe6To5Window(string day, int year, int month)
    {
        AccountingPeriod.RequestedMonth(DateOnly.Parse(day, CultureInfo.InvariantCulture)).ShouldBe((year, month));
    }

    [Fact]
    public void Deadline_IsThe5thOfTheFollowingMonth()
    {
        AccountingPeriod.Deadline(2026, 9).ShouldBe(new DateOnly(2026, 10, 5));
        AccountingPeriod.Deadline(2026, 12).ShouldBe(new DateOnly(2027, 1, 5));
    }

    [Fact]
    public void CollectionWindow_RunsFrom6thMidnightToNext6thMidnight_RomanianTime()
    {
        (DateTime start, DateTime end) = AccountingPeriod.CollectionWindowUtc(2026, 10);

        // 6 octombrie 00:00 în România (UTC+3, ora de vară) = 5 octombrie 21:00 UTC.
        start.ShouldBe(new DateTime(2026, 10, 5, 21, 0, 0, DateTimeKind.Utc));
        // 6 noiembrie 00:00 în România (UTC+2, ora de iarnă din 25 octombrie) = 5 noiembrie 22:00 UTC.
        end.ShouldBe(new DateTime(2026, 11, 5, 22, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void NotificationText_NamesTheEndedMonthAndTheDeadline()
    {
        // 1 noiembrie 2026, 08:00 în România.
        string text = RecurringDocumentationTexts.BuildNotificationText(new DateTime(2026, 11, 1, 6, 0, 0, DateTimeKind.Utc));

        text.ShouldStartWith("Luna octombrie s-a încheiat!");
        text.ShouldContain("până pe 5 ale lunii");
        text.ShouldContain("CUI-ul PFA-ului");
    }
}
