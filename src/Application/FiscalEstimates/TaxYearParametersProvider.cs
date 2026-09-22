using System.Reflection;
using System.Text.Json;

namespace Application.FiscalEstimates;

/// <summary>
/// Parametrii fiscali pe ani, citiți o singură dată din fișierele JSON incluse în asamblare.
/// Un an fără fișier nu are parametri: motorul întoarce <c>RULE_UNAVAILABLE</c>, nu refolosește
/// pragurile altui an.
/// </summary>
public sealed class TaxYearParametersProvider
{
    private readonly Dictionary<int, TaxYearParameters> _byYear;

    public TaxYearParametersProvider()
        : this(LoadEmbedded())
    {
    }

    /// <remarks>
    /// Intern, pentru teste: un constructor public cu <c>IEnumerable</c> e ales de containerul DI, care
    /// îl rezolvă cu o listă goală, iar anul rămâne fără parametri.
    /// </remarks>
    internal TaxYearParametersProvider(IEnumerable<TaxYearParameters> parameters)
    {
        _byYear = parameters.ToDictionary(p => p.TaxYear);
    }

    public TaxYearParameters? For(int taxYear) => _byYear.GetValueOrDefault(taxYear);

    private static List<TaxYearParameters> LoadEmbedded()
    {
        Assembly assembly = typeof(TaxYearParametersProvider).Assembly;
        var result = new List<TaxYearParameters>();

        foreach (string name in assembly.GetManifestResourceNames()
                     .Where(n => n.Contains(".FiscalEstimates.Parameters.tax-", StringComparison.Ordinal)
                         && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            using Stream stream = assembly.GetManifestResourceStream(name)!;
            TaxYearParameters parameters = JsonSerializer.Deserialize<TaxYearParameters>(stream)
                ?? throw new InvalidOperationException($"Parametrii fiscali din {name} nu se pot citi.");
            result.Add(parameters);
        }

        return result;
    }
}
