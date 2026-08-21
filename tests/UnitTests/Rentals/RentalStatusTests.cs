using Application.Rentals;
using Domain.Rentals;
using Shouldly;
using Xunit;

namespace UnitTests.Rentals;

/// <summary>
/// Statusul se derivă, nu se stochează. Un status stocat ar fi avut nevoie de un job la fiecare
/// miezul nopții ca să treacă închirierile în „încheiată", iar între rulări ar fi mințit.
/// </summary>
public class RentalStatusTests
{
    private static readonly DateTime Now = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

    private static Rental Rental(int startsInDays, int endsInDays, DateTime? closedAt = null) => new()
    {
        StartAtUtc = Now.AddDays(startsInDays),
        EndAtUtc = Now.AddDays(endsInDays),
        ClosedAtUtc = closedAt,
    };

    [Fact]
    public void BeforeStart_IsUpcoming()
    {
        RentalStatus.For(Rental(startsInDays: 3, endsInDays: 60), Now).ShouldBe(RentalStatus.Upcoming);
    }

    [Fact]
    public void InProgress_IsActive()
    {
        RentalStatus.For(Rental(startsInDays: -10, endsInDays: 50), Now).ShouldBe(RentalStatus.Active);
    }

    [Fact]
    public void WithinAWeekOfHandover_IsEndingSoon()
    {
        RentalStatus.For(Rental(startsInDays: -50, endsInDays: 5), Now).ShouldBe(RentalStatus.EndingSoon);
    }

    [Fact]
    public void ExactlyAtTheEndingSoonBoundary_IsEndingSoon()
    {
        RentalStatus.For(Rental(startsInDays: -50, endsInDays: RentalStatus.EndingSoonDays), Now)
            .ShouldBe(RentalStatus.EndingSoon);
    }

    [Fact]
    public void PastEndDate_IsCompleted()
    {
        RentalStatus.For(Rental(startsInDays: -90, endsInDays: -1), Now).ShouldBe(RentalStatus.Completed);
    }

    /// <summary>Predarea anticipată încheie închirierea, chiar dacă data planificată e în viitor.</summary>
    [Fact]
    public void ClosedEarly_IsCompletedEvenWhileStillScheduled()
    {
        RentalStatus.For(Rental(startsInDays: -10, endsInDays: 40, closedAt: Now.AddDays(-1)), Now)
            .ShouldBe(RentalStatus.Completed);
    }

    /// <summary>O rezervare viitoare anulată nu apare ca „urmează".</summary>
    [Fact]
    public void ClosedBeforeStarting_IsCompleted()
    {
        RentalStatus.For(Rental(startsInDays: 5, endsInDays: 60, closedAt: Now), Now)
            .ShouldBe(RentalStatus.Completed);
    }
}
