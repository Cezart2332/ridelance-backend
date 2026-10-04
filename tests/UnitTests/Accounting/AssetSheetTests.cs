using Application.Accounting.Assets;
using Application.Accounting.Contracts;
using Domain.Accounting;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>QA 18–19: fișa mijlocului fix — starea, denumirea și planul de amortizare.</summary>
public sealed class AssetSheetTests
{
    private static PfaAsset Laptop(DateOnly? inService) => new()
    {
        Id = Guid.NewGuid(),
        PfaRegistrationId = Guid.NewGuid(),
        InventoryNumber = "MF-0001",
        Name = "Laptop Acer — .",
        Kind = AssetKind.FixedAsset,
        Status = AssetStatus.Active,
        DocumentRef = "Factura 12",
        EntryDate = new DateOnly(2026, 6, 1),
        InServiceDate = inService,
        EntryValue = 3600m,
        DepreciationClassCode = "2.2.9",
        NormalLifeMonths = 36,
    };

    /// <summary>QA 19: fără punere în funcțiune, activul e „în clasificare”, chiar dacă starea salvată e activă.</summary>
    [Fact]
    public void QA19_AnActiveAssetWithoutInServiceDateIsInClassification()
    {
        AssetDto dto = AssetSupport.Dto(Laptop(null), [], new DateOnly(2026, 10, 1), null);
        (dto.Status, dto.Name).ShouldBe((AssetStatus.PendingClassification, "Laptop Acer"));
    }

    /// <summary>QA 19: denumirea fără separatori de la câmpuri goale; un activ complet are planul lui.</summary>
    [Theory]
    [InlineData("Laptop Acer — .", "Laptop Acer")]
    [InlineData("Laptop Acer - ", "Laptop Acer")]
    [InlineData("Laptop Acer", "Laptop Acer")]
    public void QA19_NamesWithoutEmptySeparators(string raw, string clean) => AssetSupport.CleanName(raw).ShouldBe(clean);

    [Fact]
    public void QA19_ACompleteAssetHasADepreciationPlan()
    {
        List<Depreciation.PlannedLine> plan = Depreciation.Plan(Laptop(new DateOnly(2026, 6, 15)), DepreciationStart.NextMonth, []);
        plan.Count.ShouldBe(36);
        (plan[0].Year, plan[0].Month, plan[0].Amount).ShouldBe((2026, 7, 100m));
        plan[^1].Remaining.ShouldBe(0m);
    }
}
