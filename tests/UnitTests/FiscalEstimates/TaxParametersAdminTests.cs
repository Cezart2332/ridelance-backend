using Application.Abstractions.Authentication;
using Application.Admin.TaxParameters;
using Application.FiscalEstimates;
using Application.PfaRegistrations;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.FiscalEstimates;

/// <summary>
/// Plafoanele fiscale se pot schimba din admin: valorile de acolo bat fișierul anului, ajung și
/// în calculatorul din dashboard, iar resetarea le întoarce la fișier.
/// </summary>
public sealed class TaxParametersAdminTests
{
    private static readonly TaxParametersValues Raised = new(
        MinWageReference: 4_325,
        CassMinThreshold: 25_950,
        CasThreshold12: 51_900,
        CasThreshold24: 103_800,
        CassMaxBase: 311_400,
        CasRate: 0.25m,
        CassRate: 0.10m,
        IncomeTaxRate: 0.10m,
        CarriedLossOffsetLimit: 0.70m);

    [Fact]
    public async Task Salvarea_bate_fisierul_si_schimba_versiunea_regulii()
    {
        using ApplicationDbContext db = NewDb();
        var provider = new TaxYearParametersProvider();
        string before = provider.For(2026)!.RuleVersion;

        Result<TaxParametersResponse> result = await new UpdateTaxParametersCommandHandler(db, provider, new StubUser())
            .Handle(new UpdateTaxParametersCommand(2026, Raised), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsOverridden.ShouldBeTrue();
        result.Value.Current!.MinWageReference.ShouldBe(4_325);
        result.Value.Defaults!.MinWageReference.ShouldBe(4_050);
        provider.For(2026)!.CasThreshold12.ShouldBe(51_900);
        // Versiunea nouă e cea după care coada recalculează estimările făcute cu cea veche.
        provider.For(2026)!.RuleVersion.ShouldNotBe(before);
        (await db.AppSettings.SingleAsync()).Key.ShouldBe("fiscal.tax-parameters.2026");
    }

    [Fact]
    public async Task Resetarea_intoarce_valorile_din_fisier()
    {
        using ApplicationDbContext db = NewDb();
        var provider = new TaxYearParametersProvider();
        await new UpdateTaxParametersCommandHandler(db, provider, new StubUser())
            .Handle(new UpdateTaxParametersCommand(2026, Raised), CancellationToken.None);

        Result<TaxParametersResponse> reset = await new ResetTaxParametersCommandHandler(db, provider)
            .Handle(new ResetTaxParametersCommand(2026), CancellationToken.None);

        reset.Value.IsOverridden.ShouldBeFalse();
        provider.For(2026)!.MinWageReference.ShouldBe(4_050);
        (await db.AppSettings.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(500, "Salariul minim")]
    [InlineData(4_325, "să crească")]
    public async Task Valori_absurde_sunt_refuzate(decimal minWage, string message)
    {
        using ApplicationDbContext db = NewDb();
        TaxParametersValues values = minWage == 500
            ? Raised with { MinWageReference = 500 }
            : Raised with { CasThreshold24 = 40_000 };

        Result<TaxParametersResponse> result = await new UpdateTaxParametersCommandHandler(db, new TaxYearParametersProvider(), new StubUser())
            .Handle(new UpdateTaxParametersCommand(2026, values), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Description.ShouldContain(message);
    }

    [Fact]
    public void Cota_de_100_la_suta_e_refuzata()
    {
        TaxParametersAdmin.Validate(Raised with { CassRate = 1m }).ShouldNotBeNull();
        TaxParametersAdmin.Validate(Raised).ShouldBeNull();
    }

    [Fact]
    public void Sincronizarea_scoate_anii_resetati_pe_alta_instanta()
    {
        var provider = new TaxYearParametersProvider();
        TaxYearParameters custom = provider.For(2026)! with { MinWageReference = 4_325, RuleVersion = "x" };
        provider.ApplyOverrides(new Dictionary<int, TaxYearParameters> { [2026] = custom });
        provider.For(2026)!.MinWageReference.ShouldBe(4_325);

        provider.ApplyOverrides(new Dictionary<int, TaxYearParameters>());

        provider.For(2026)!.MinWageReference.ShouldBe(4_050);
    }

    [Fact]
    public void Calculatorul_din_dashboard_foloseste_plafoanele_din_admin()
    {
        TaxYearParameters raised = new TaxYearParametersProvider().For(2026)! with { CasThreshold12 = 51_900, CasThreshold24 = 103_800 };

        // Un profit de 50.000: peste pragul legal de 12 salarii (48.600), sub cel ridicat.
        PfaTaxCalculator.Compute(50_000, 0, 2026).Cas.ShouldBe(12_150);
        PfaTaxCalculator.Compute(50_000, 0, 2026, raised).Cas.ShouldBe(0);
        PfaTaxCalculator.ComputeThresholdProgress(50_000, 0, 2026, raised).CasFirstThreshold.ShouldBe(51_900);
    }

    private static ApplicationDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new Events());

    private sealed class StubUser : IUserContext
    {
        public Guid UserId { get; } = Guid.NewGuid();
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
