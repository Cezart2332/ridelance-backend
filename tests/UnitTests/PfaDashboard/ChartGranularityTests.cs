using Application.PfaDashboard;
using Shouldly;
using Xunit;

namespace UnitTests.PfaDashboard;

/// <summary>Axa graficelor: săptămâna pe zile, luna pe săptămâni, anul pe luni.</summary>
public sealed class ChartGranularityTests
{
    [Theory]
    [InlineData(1, "day")]
    [InlineData(7, "day")]
    [InlineData(8, "week")]
    [InlineData(31, "week")]
    [InlineData(62, "week")]
    [InlineData(63, "month")]
    [InlineData(365, "month")]
    public void Granularitatea_dupa_lungimea_perioadei(int days, string expected) =>
        GetPfaDashboardSummaryQueryHandler.GranularityFor(days).ShouldBe(expected);

    [Fact]
    public void Luna_se_imparte_pe_saptamani_calendaristice_cu_etichete_de_interval()
    {
        // Septembrie 2026 începe marți: prima săptămână e 1–6 sep, ultima 28–30 sep.
        var september = new PfaDashboardPeriod(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        List<PfaDashboardPeriod> weeks = GetPfaDashboardSummaryQueryHandler.WeekBuckets(september);

        weeks.Count.ShouldBe(5);
        weeks.Select(w => GetPfaDashboardSummaryQueryHandler.BucketLabel("week", w))
            .ShouldBe(["1–6 sep", "7–13 sep", "14–20 sep", "21–27 sep", "28–30 sep"]);
        weeks.Sum(w => w.DayCount).ShouldBe(30);
    }

    [Fact]
    public void Saptamana_peste_doua_luni_are_eticheta_cu_ambele_luni()
    {
        var period = new PfaDashboardPeriod(new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 11));
        GetPfaDashboardSummaryQueryHandler.BucketLabel("week", GetPfaDashboardSummaryQueryHandler.WeekBuckets(period)[0])
            .ShouldBe("28 sep–4 oct");
    }

    [Fact]
    public void Zilele_saptamanii_si_lunile_anului()
    {
        var monday = new DateOnly(2026, 9, 21);
        Enumerable.Range(0, 7)
            .Select(i => GetPfaDashboardSummaryQueryHandler.BucketLabel("day", new PfaDashboardPeriod(monday.AddDays(i), monday.AddDays(i))))
            .ShouldBe(["Lun", "Mar", "Mie", "Joi", "Vin", "Sâm", "Dum"]);
        GetPfaDashboardSummaryQueryHandler.BucketLabel("month", new PfaDashboardPeriod(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)))
            .ShouldBe("Noi");
    }
}
