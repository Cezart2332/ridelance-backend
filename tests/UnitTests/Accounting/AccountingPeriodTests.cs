using System.Globalization;
using Application.Accounting;
using Application.Notifications.RecurringDocumentation;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>
/// Luna contabilă: documentele pe septembrie se cer de pe 26 septembrie până pe 25 octombrie.
/// </summary>
public sealed class AccountingPeriodTests
{
    [Theory]
    [InlineData("2026-09-25", 2026, 8)]   // fereastra lui august e încă deschisă
    [InlineData("2026-09-26", 2026, 9)]   // se deschide septembrie
    [InlineData("2026-10-25", 2026, 9)]   // ultima zi pentru septembrie
    [InlineData("2026-10-26", 2026, 10)]
    [InlineData("2026-01-10", 2025, 12)]  // peste an
    [InlineData("2026-12-31", 2026, 12)]
    public void RequestedMonth_FollowsThe26To25Window(string day, int year, int month)
    {
        AccountingPeriod.RequestedMonth(DateOnly.Parse(day, CultureInfo.InvariantCulture)).ShouldBe((year, month));
    }

    [Fact]
    public void Deadline_IsThe25thOfTheFollowingMonth()
    {
        AccountingPeriod.Deadline(2026, 9).ShouldBe(new DateOnly(2026, 10, 25));
        AccountingPeriod.Deadline(2026, 12).ShouldBe(new DateOnly(2027, 1, 25));
    }

    [Fact]
    public void CollectionWindow_RunsFrom26thMidnightToNext26thMidnight_RomanianTime()
    {
        (DateTime start, DateTime end) = AccountingPeriod.CollectionWindowUtc(2026, 9);

        // 26 septembrie 00:00 în România (UTC+3, ora de vară) = 25 septembrie 21:00 UTC.
        start.ShouldBe(new DateTime(2026, 9, 25, 21, 0, 0, DateTimeKind.Utc));
        // 26 octombrie 00:00 în România (UTC+2, ora de iarnă din 25 octombrie) = 25 octombrie 22:00 UTC.
        end.ShouldBe(new DateTime(2026, 10, 25, 22, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void NotificationText_NamesTheMonthAndTheDeadline()
    {
        // 26 septembrie 2026, 08:00 în România.
        string text = RecurringDocumentationTexts.BuildNotificationText(new DateTime(2026, 9, 26, 5, 0, 0, DateTimeKind.Utc));

        text.ShouldContain("septembrie 2026");
        text.ShouldContain("până pe 25 octombrie");
    }
}
