using NetArchTest.Rules;
using Shouldly;

namespace ArchitectureTests.Layers;

/// <summary>Spec registre §9, criteriul de acceptanță: REF nu are nicio dependență de cod pe RJIP.</summary>
public class RegisterTests : BaseTest
{
    [Fact]
    public void Ref_Should_NotDependOnRjip()
    {
        TestResult result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespace("Application.Accounting.FiscalRegister")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Application.Accounting.Registers.GetRjipQuery",
                "Application.Accounting.Registers.GetRjipQueryHandler",
                "Application.Accounting.Registers.ExportRjipQuery",
                "Application.Accounting.Registers.ExportRjipQueryHandler",
                "Application.Accounting.Contracts.RjipView",
                "Application.Accounting.Contracts.RjipRow",
                "Application.Accounting.Contracts.RjipMonthTotal")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }
}
