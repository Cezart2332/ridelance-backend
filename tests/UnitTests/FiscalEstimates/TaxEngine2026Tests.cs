using Application.FiscalEstimates;
using Shouldly;
using Xunit;

namespace UnitTests.FiscalEstimates;

/// <summary>
/// Testele obligatorii din SPEC_TAXE_ESTIMATE_PFA §12. Presupuneri: acoperire completă, fără alte
/// venituri, pierderi, contribuții voluntare sau plăți anterioare, dacă nu e spus altfel.
/// </summary>
public sealed class TaxEngine2026Tests
{
    private static readonly TaxYearParameters P2026 = new TaxYearParametersProvider().For(2026)!;
    private static readonly TaxEngine2026 Engine = new();

    private static TaxInput Input(decimal net, ProfileFlags? flags = null, decimal payments = 0, decimal? reserve = null, int weeksLeft = 10) =>
        new(new IncomeProjection(net, null, [], net, 1000, 8, weeksLeft), flags ?? new ProfileFlags(), payments, reserve, weeksLeft);

    private static TaxResult Run(decimal net, ProfileFlags? flags = null, decimal payments = 0, decimal? reserve = null) =>
        Engine.Calculate(Input(net, flags, payments, reserve), P2026);

    private static ComponentResult C(TaxResult r, string component) => r.Components.Single(c => c.Component == component);

    [Fact]
    public void Parametrii_2026_vin_din_configuratie()
    {
        P2026.RuleVersion.ShouldBe("2026.1");
        P2026.CassMinThreshold.ShouldBe(24_300);
        P2026.CasThreshold12.ShouldBe(48_600);
        P2026.CasThreshold24.ShouldBe(97_200);
        P2026.CassMaxBase.ShouldBe(291_600);
    }

    // ── §12.1 Scenarii de referință ─────────────────────────────────────────

    public static TheoryData<string, decimal, decimal, decimal, decimal, decimal> Reference => new()
    {
        { "A", 20_000, 0, 2_430, 1_800, 4_230 },
        { "B", 20_000, 0, 2_000, 1_800, 3_800 },
        { "C", 60_000, 12_150, 6_000, 4_185, 22_335 },
        { "D", 60_000, 0, 6_000, 5_400, 11_400 },
        { "E", 100_000, 24_300, 10_000, 6_570, 40_870 },
        { "F", 0, 0, 0, 0, 0 },
    };

    [Theory]
    [MemberData(nameof(Reference))]
    public void Scenarii_de_referinta(string scenario, decimal net, decimal cas, decimal cass, decimal tax, decimal total)
    {
        ProfileFlags flags = scenario switch
        {
            "B" => new ProfileFlags { SalariedCassExempt = true },
            "D" => new ProfileFlags { PensionerFullYear = true },
            _ => new ProfileFlags(),
        };

        TaxResult r = Run(net, flags);

        C(r, TaxComponents.Cas).Amount.ShouldBe(cas);
        C(r, TaxComponents.Cass).Amount.ShouldBe(cass);
        C(r, TaxComponents.IncomeTax).Amount.ShouldBe(tax);
        r.Reserve.AnnualEstimated.ShouldBe(total);
        r.Reserve.Status.ShouldBe(TaxStatuses.Estimated);
    }

    // ── §12.2 Praguri ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(24_299, 0, 2_430, 2_187, 4_617, "minimum")]
    [InlineData(24_300, 0, 2_430, 2_187, 4_617, "rate")]
    [InlineData(48_599, 0, 4_860, 4_374, 9_234, "rate")]
    [InlineData(48_600, 12_150, 4_860, 3_159, 20_169, "rate")]
    [InlineData(97_199, 12_150, 9_720, 7_533, 29_403, "rate")]
    [InlineData(97_200, 24_300, 9_720, 6_318, 40_338, "rate")]
    [InlineData(291_600, 24_300, 29_160, 23_814, 77_274, "rate")]
    [InlineData(291_601, 24_300, 29_160, 23_814, 77_274, "rate")]
    [InlineData(300_000, 24_300, 29_160, 24_654, 78_114, "rate")]
    public void Praguri(double net, double cas, double cass, double tax, double total, string cassBranch)
    {
        TaxResult r = Run((decimal)net);

        C(r, TaxComponents.Cas).Amount.ShouldBe((decimal)cas);
        C(r, TaxComponents.Cass).Amount.ShouldBe((decimal)cass);
        C(r, TaxComponents.IncomeTax).Amount.ShouldBe((decimal)tax);
        r.Reserve.AnnualEstimated.ShouldBe((decimal)total);
        // La 24.299 și 24.300 suma e aceeași; contează ramura din care vine.
        C(r, TaxComponents.Cass).Breakdown["branch"].ShouldBe(cassBranch);
    }

    // ── §12.3 Cazuri de status ──────────────────────────────────────────────

    [Fact]
    public void Alte_venituri_sub_prag_cer_clarificare_doar_la_CASS()
    {
        TaxResult r = Run(20_000, new ProfileFlags { OtherIncome = true });

        C(r, TaxComponents.Cass).Status.ShouldBe(TaxStatuses.RequiresClarification);
        C(r, TaxComponents.Cass).Amount.ShouldBeNull();
        C(r, TaxComponents.Cass).ReasonCode.ShouldBe(TaxReasons.CassExceptionUnknown);
        C(r, TaxComponents.IncomeTax).Status.ShouldBe(TaxStatuses.Estimated);
        C(r, TaxComponents.IncomeTax).Amount.ShouldBe(1_800);
        r.Reserve.Status.ShouldBe(TaxStatuses.Partial);
        r.Reserve.Missing.ShouldBe([TaxComponents.Cass]);
    }

    [Fact]
    public void Alte_venituri_peste_prag_nu_conteaza()
    {
        TaxResult r = Run(60_000, new ProfileFlags { OtherIncome = true });
        C(r, TaxComponents.Cass).Status.ShouldBe(TaxStatuses.Estimated);
        C(r, TaxComponents.Cass).Amount.ShouldBe(6_000);
    }

    [Fact]
    public void Pensionar_devenit_in_cursul_anului()
    {
        TaxResult high = Run(30_000, new ProfileFlags { PensionerMidYear = true });
        C(high, TaxComponents.Cas).Status.ShouldBe(TaxStatuses.RequiresClarification);
        C(high, TaxComponents.IncomeTax).Status.ShouldBe(TaxStatuses.RequiresClarification);
        C(high, TaxComponents.Cass).Status.ShouldBe(TaxStatuses.Estimated);
        C(high, TaxComponents.Cass).Amount.ShouldBe(3_000);

        TaxResult low = Run(20_000, new ProfileFlags { PensionerMidYear = true });
        C(low, TaxComponents.Cass).Status.ShouldBe(TaxStatuses.RequiresClarification);
    }

    [Fact]
    public void Pensionar_si_salariat_exceptiile_se_cumuleaza()
    {
        TaxResult r = Run(20_000, new ProfileFlags { PensionerFullYear = true, SalariedCassExempt = true });
        C(r, TaxComponents.Cas).Amount.ShouldBe(0);
        C(r, TaxComponents.Cass).Amount.ShouldBe(2_000);
    }

    [Fact]
    public void Studentul_nu_primeste_CASS_zero()
    {
        TaxResult r = Run(30_000, new ProfileFlags { StudentCassExempt = true });
        C(r, TaxComponents.Cas).Amount.ShouldBe(0);
        C(r, TaxComponents.Cass).Amount.ShouldBe(3_000);
    }

    [Fact]
    public void Salariat_sub_pragul_CASS_plateste_minimul()
    {
        // salaryAboveCassMin = no: nici excepție, nici necunoscut.
        TaxResult r = Run(20_000, new ProfileFlags());
        C(r, TaxComponents.Cass).Amount.ShouldBe(2_430);
    }

    [Fact]
    public void CAS_voluntar_pe_baza_declarata()
    {
        TaxResult r = Run(30_000, new ProfileFlags { CasVoluntaryBase = 60_000 });
        C(r, TaxComponents.Cas).Amount.ShouldBe(15_000);
    }

    [Fact]
    public void Gol_in_acoperire_nu_inseamna_zero()
    {
        IncomeProjection projection = IncomeProjector.Project(new FinancialSnapshot(
            Guid.NewGuid(),
            new DateOnly(2026, 10, 15),
            2026,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 9, 1),
            Months(9, 10, income: 8_000),
            0));

        // Ianuarie–august lipsesc: nu sunt zero, ci estimate din media lui septembrie, la fel ca restul anului.
        decimal weekly = 8_000m / 30 * 7;
        decimal uncoveredWeeks = (new DateOnly(2026, 9, 1).DayNumber - new DateOnly(2026, 1, 1).DayNumber) / 7m;
        decimal weeksRemaining = (new DateOnly(2026, 12, 31).DayNumber - new DateOnly(2026, 10, 15).DayNumber) / 7m;
        projection.UncoveredPeriod.ShouldBe("01.01.2026 – 31.08.2026");
        projection.NetAnnualEstimated.ShouldBe(Math.Round(16_000 + weekly * (weeksRemaining + uncoveredWeeks), 2));

        TaxResult r = Engine.Calculate(new TaxInput(projection, new ProfileFlags(), 0, null, 11), P2026);

        r.Components.Take(3).ShouldAllBe(c => c.Status == TaxStatuses.Estimated && c.Amount > 0);
        r.Warnings.ShouldContain(TaxWarnings.CoverageGap);
        r.Reserve.Status.ShouldBe(TaxStatuses.Estimated);
        r.Reserve.Total.ShouldNotBeNull();
    }

    [Fact]
    public void PFA_infiintat_in_septembrie_proiecteaza_fara_proratare()
    {
        // Înființat 01.09, septembrie cu ~1.000 lei net pe săptămână.
        decimal september = 1_000m / 7 * 30;
        IncomeProjection projection = IncomeProjector.Project(new FinancialSnapshot(
            Guid.NewGuid(),
            new DateOnly(2026, 10, 6),
            2026,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 1),
            [new MonthFigures(9, september, 0), new MonthFigures(10, 700, 0)],
            0));

        decimal weeksRemaining = (new DateOnly(2026, 12, 31).DayNumber - new DateOnly(2026, 10, 6).DayNumber) / 7m;
        projection.WeeklyAverage.ShouldBe(1_000);
        projection.NetAnnualEstimated.ShouldBe(Math.Round(september + 700 + 1_000 * weeksRemaining, 2));

        // Plafoanele nu se proratează: sub 24.300 anual rămâne CASS minim întreg.
        TaxResult r = Engine.Calculate(new TaxInput(projection, new ProfileFlags(), 0, null, 13), P2026);
        C(r, TaxComponents.Cass).Amount.ShouldBe(2_430);
    }

    [Fact]
    public void Sub_patru_saptamani_de_istoric()
    {
        IncomeProjection projection = IncomeProjector.Project(new FinancialSnapshot(
            Guid.NewGuid(),
            new DateOnly(2026, 10, 2),
            2026,
            new DateOnly(2026, 9, 10),
            new DateOnly(2026, 9, 10),
            [new MonthFigures(9, 3_000, 0)],
            0));

        projection.UnavailableReason.ShouldBe(TaxReasons.ShortHistory);
        Engine.Calculate(new TaxInput(projection, new ProfileFlags(), 0, null, 13), P2026)
            .Components[0].ReasonCode.ShouldBe(TaxReasons.ShortHistory);
    }

    [Fact]
    public void Venit_zero_cu_acoperire_e_zero_fara_import_e_date_insuficiente()
    {
        TaxResult zero = Run(0);
        zero.Components.Take(3).ShouldAllBe(c => c.Status == TaxStatuses.Estimated && c.Amount == 0);

        IncomeProjection none = IncomeProjector.Project(new FinancialSnapshot(
            Guid.NewGuid(), new DateOnly(2026, 6, 1), 2026, new DateOnly(2026, 1, 1), null, [], 0));
        Engine.Calculate(new TaxInput(none, new ProfileFlags(), 0, null, 30), P2026)
            .Components[0].Status.ShouldBe(TaxStatuses.InsufficientData);
    }

    [Fact]
    public void Platile_si_rezerva_existenta_scad_din_ce_e_de_pus_deoparte()
    {
        TaxResult r = Run(20_000, payments: 1_000, reserve: 500);
        r.Reserve.AnnualEstimated.ShouldBe(4_230);
        r.Reserve.Total.ShouldBe(2_730);
        r.Reserve.Weekly.ShouldBe(273);
        r.Reserve.ExistingReserveAssumedZero.ShouldBeFalse();
    }

    [Fact]
    public void Plati_declarate_dar_neinregistrate_cer_clarificare()
    {
        TaxResult r = Run(20_000, new ProfileFlags { TaxPaymentsMade = true });
        r.Reserve.Status.ShouldBe(TaxStatuses.RequiresClarification);
        r.Reserve.ReasonCode.ShouldBe(TaxReasons.TaxPaymentsMissing);
        r.Reserve.Total.ShouldBeNull();
    }

    [Fact]
    public void An_fara_parametri()
    {
        TaxResult r = Engine.Calculate(Input(20_000), new TaxYearParametersProvider().For(2027));
        r.Components.Take(3).ShouldAllBe(c => c.Status == TaxStatuses.RuleUnavailable && c.Amount == null);
        r.Reserve.Status.ShouldBe(TaxStatuses.RuleUnavailable);
    }

    [Fact]
    public void Taxele_platformelor_nu_sunt_configurate_si_nu_intra_in_total()
    {
        TaxResult r = Run(20_000);
        ComponentResult platform = C(r, TaxComponents.PlatformTaxes);
        platform.Status.ShouldBe(TaxStatuses.NotConfigured);
        platform.Amount.ShouldBeNull();
        r.Reserve.AnnualEstimated.ShouldBe(4_230);
    }

    [Fact]
    public void Corectura_deschisa_opreste_calculul()
    {
        TaxResult r = Run(20_000, new ProfileFlags { PendingCorrection = true });
        r.Components.Take(3).ShouldAllBe(c => c.ReasonCode == TaxReasons.DataCorrectionPending && c.Amount == null);
    }

    [Theory]
    [InlineData(44_000, true)]
    [InlineData(40_000, false)]
    [InlineData(90_000, true)]
    public void Avertisment_aproape_de_plafonul_CAS(double net, bool near)
    {
        Run((decimal)net).Warnings.Contains(TaxWarnings.CasThresholdNear).ShouldBe(near);
    }

    [Fact]
    public void Alte_activitati_independente_si_strainatate_cer_clarificare()
    {
        Run(60_000, new ProfileFlags { OtherIndependent = true }).Components.Take(3)
            .ShouldAllBe(c => c.Status == TaxStatuses.RequiresClarification && c.ReasonCode == TaxReasons.OtherIndependentTotal);
        Run(60_000, new ProfileFlags { CrossBorder = true }).Components.Take(3)
            .ShouldAllBe(c => c.ReasonCode == TaxReasons.CrossBorder);
        C(Run(60_000, new ProfileFlags { CarriedLosses = true }), TaxComponents.IncomeTax).ReasonCode.ShouldBe(TaxReasons.CarriedLosses);
        C(Run(60_000, new ProfileFlags { CassOptIn = true }), TaxComponents.Cass).ReasonCode.ShouldBe(TaxReasons.CassOptIn);
    }

    [Fact]
    public void Toate_de_clarificat_inseamna_rezerva_de_clarificat_nu_date_insuficiente()
    {
        TaxResult r = Run(60_000, new ProfileFlags { OtherIndependent = true });
        r.Reserve.Status.ShouldBe(TaxStatuses.RequiresClarification);
        r.Reserve.Total.ShouldBeNull();
    }

    [Fact]
    public void Netul_altor_activitati_intra_in_plafonul_CAS()
    {
        // PFA 30.000 + alte activități 30.000 = 60.000 ⇒ CAS pe 12 salarii, CASS doar pe profitul PFA.
        TaxResult r = Run(30_000, new ProfileFlags { OtherIndependent = true, OtherIndependentNetAnnual = 30_000 });
        C(r, TaxComponents.Cas).Amount.ShouldBe(12_150);
        C(r, TaxComponents.Cass).Amount.ShouldBe(3_000);
        C(r, TaxComponents.IncomeTax).Amount.ShouldBe(1_485);
        r.Reserve.Status.ShouldBe(TaxStatuses.Estimated);
    }

    [Fact]
    public void Cu_alte_activitati_peste_prag_nu_se_completeaza_CASS_la_minim()
    {
        TaxResult r = Run(20_000, new ProfileFlags { OtherIndependent = true, OtherIndependentNetAnnual = 10_000 });
        C(r, TaxComponents.Cass).Amount.ShouldBe(2_000);
        C(r, TaxComponents.Cass).Breakdown["branch"].ShouldBe("combined");
        C(r, TaxComponents.Cas).Amount.ShouldBe(0);
    }

    [Theory]
    [InlineData(true, 2_000)]
    [InlineData(false, 2_430)]
    public void CASS_pe_alte_venituri_raspuns_cunoscut(bool insured, int cass)
    {
        C(Run(20_000, new ProfileFlags { OtherIncome = true, OtherIncomeCassInsured = insured }), TaxComponents.Cass)
            .Amount.ShouldBe(cass);
    }

    [Fact]
    public void CASS_pe_alte_venituri_necunoscut_cere_clarificare()
    {
        ComponentResult cass = C(Run(20_000, new ProfileFlags { OtherIncome = true }), TaxComponents.Cass);
        cass.Status.ShouldBe(TaxStatuses.RequiresClarification);
        cass.MissingInputs.ShouldBe(["otherIncomeCassInsured"]);
    }

    [Theory]
    [InlineData(24_300, 2_430)]
    [InlineData(50_000, 5_000)]
    public void Optiunea_CASS_plateste_maximul_dintre_baza_aleasa_si_profit(int optInBase, int cass)
    {
        // Net 10.000: pe profit ar fi minimul, 2.430.
        C(Run(10_000, new ProfileFlags { CassOptIn = true, CassOptInBase = optInBase }), TaxComponents.Cass).Amount.ShouldBe(cass);
    }

    [Theory]
    [InlineData(5_000, 1_300)]
    [InlineData(30_000, 400)]
    public void Pierderile_reportate_scad_din_baza_impozitului_in_limita_a_70_la_suta(int losses, int tax)
    {
        // Net 20.000: CAS 0, CASS deductibilă 2.000; pierderea acoperă cel mult 14.000.
        C(Run(20_000, new ProfileFlags { CarriedLosses = true, CarriedLossesAmount = losses }), TaxComponents.IncomeTax)
            .Amount.ShouldBe(tax);
    }

    private static List<MonthFigures> Months(int from, int to, decimal income) =>
        Enumerable.Range(from, to - from + 1).Select(m => new MonthFigures(m, income, 0)).ToList();
}
