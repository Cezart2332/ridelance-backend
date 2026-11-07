using Application.Bolt;
using Application.Notifications.DocumentExpiry;
using Shouldly;
using Xunit;

namespace UnitTests.Notifications;

/// <summary>
/// Pragul de expirare se ia din zilele rămase, nu trebuie nimerit la zi: un document încărcat cu
/// 5 zile rămase sau o zi fără rulare primesc totuși notificarea.
/// </summary>
public sealed class DocumentExpiryThresholdTests
{
    [Theory]
    [InlineData(45, null)]
    [InlineData(30, 30)]
    [InlineData(20, 30)]
    [InlineData(14, 14)]
    [InlineData(10, 14)]
    [InlineData(7, 7)]
    [InlineData(5, 7)]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    [InlineData(-8, null)]
    public void ThresholdFor_PicksTheClosestCrossedThreshold(int days, int? threshold)
    {
        CheckDocumentExpiryCommandHandler.ThresholdFor(days).ShouldBe(threshold);
    }

    [Fact]
    public void BoltSyncWindow_RecoversTheGapButKeepsTheWeeklyOverlap()
    {
        var now = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

        // Sincronizare la zi: 7 zile înaintea ultimei sincronizări.
        BoltSyncWindow.Start(now.AddMinutes(-5), now).ShouldBe(now.AddMinutes(-5).AddDays(-7));
        // Căzută de o lună: de la ultima sincronizare, cu suprapunere.
        BoltSyncWindow.Start(now.AddDays(-30), now).ShouldBe(now.AddDays(-37));
        // Integrare nouă: tot anul.
        BoltSyncWindow.Start(null, now).ShouldBe(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        // În ianuarie, suprapunerea trece și peste an.
        var january = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        BoltSyncWindow.Start(january.AddHours(-1), january).ShouldBe(january.AddDays(-7));
    }
}
