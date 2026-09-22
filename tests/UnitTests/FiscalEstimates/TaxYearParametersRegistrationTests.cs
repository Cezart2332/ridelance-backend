using Application;
using Application.FiscalEstimates;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace UnitTests.FiscalEstimates;

/// <summary>
/// Parametrii fiscali luați din container, ca pe server. Testele motorului construiesc providerul cu
/// <c>new</c>; în producție containerul alegea constructorul cu <c>IEnumerable</c>, îl umplea cu o
/// listă goală, iar fiecare rulare ieșea RULE_UNAVAILABLE.
/// </summary>
public sealed class TaxYearParametersRegistrationTests
{
    [Fact]
    public void ProviderFromContainer_Has2026Parameters()
    {
        using ServiceProvider services = new ServiceCollection().AddApplication().BuildServiceProvider();

        TaxYearParameters? parameters = services.GetRequiredService<TaxYearParametersProvider>().For(2026);

        parameters.ShouldNotBeNull();
        parameters.RuleVersion.ShouldBe("2026.1");
    }
}
