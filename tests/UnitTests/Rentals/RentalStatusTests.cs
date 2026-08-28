using Application.Rentals;
using Domain.Rentals;
using Shouldly;
using Xunit;

namespace UnitTests.Rentals;

/// <summary>
/// Statusul unei închirieri se citește pe zile, fiindcă perioada se alege pe zile.
/// </summary>
/// <remarks>
/// Ora din bază e prânzul UTC, pusă acolo de formular ca să aibă ce salva pentru o dată fără oră.
/// Comparată ca moment, o închiriere făcută dimineața pentru azi apărea „viitoare" până la 12:00 —
/// deci lipsea din lista celor active chiar în ziua în care fusese creată.
/// </remarks>
public sealed class RentalStatusTests
{
    private static Rental Rental(DateTime start, DateTime end) => new()
    {
        Id = Guid.NewGuid(),
        Lifecycle = RentalLifecycle.Confirmed,
        StartAtUtc = start,
        EndAtUtc = end,
    };

    /// <summary>Dimineața zilei în care se face închirierea, înainte de prânzul din date.</summary>
    private static readonly DateTime Morning = new(2026, 8, 28, 9, 0, 0, DateTimeKind.Utc);

    private static DateTime NoonOn(int day) => new(2026, 8, day, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Un final destul de departe cât să nu intre în fereastra „se apropie predarea".</summary>
    private static readonly DateTime FarEnd = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void O_inchiriere_care_incepe_azi_e_activa_de_dimineata()
    {
        Rental rental = Rental(NoonOn(28), FarEnd);

        RentalStatus.For(rental, Morning).ShouldBe(RentalStatus.Active);
    }

    [Fact]
    public void Ultima_zi_de_inchiriere_e_o_zi_intreaga()
    {
        // Se încheie azi la prânz; la 21:00 tot azi, încă nu e încheiată.
        Rental rental = Rental(NoonOn(20), NoonOn(28));
        var evening = new DateTime(2026, 8, 28, 21, 0, 0, DateTimeKind.Utc);

        RentalStatus.For(rental, evening).ShouldBe(RentalStatus.EndingSoon);
    }

    [Fact]
    public void O_inchiriere_care_incepe_maine_e_viitoare()
    {
        Rental rental = Rental(NoonOn(29), FarEnd);

        RentalStatus.For(rental, Morning).ShouldBe(RentalStatus.Upcoming);
    }

    [Fact]
    public void O_inchiriere_terminata_ieri_e_incheiata()
    {
        Rental rental = Rental(NoonOn(20), NoonOn(27));

        RentalStatus.For(rental, Morning).ShouldBe(RentalStatus.Completed);
    }

    [Fact]
    public void Deciziile_bat_calendarul()
    {
        Rental cancelled = Rental(NoonOn(28), FarEnd);
        cancelled.Lifecycle = RentalLifecycle.Cancelled;

        RentalStatus.For(cancelled, Morning).ShouldBe(RentalStatus.Cancelled);
    }
}
