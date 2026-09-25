using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace Application.FiscalEstimates;

/// <summary>
/// Parametrii fiscali pe ani (salariul minim, plafoanele CAS/CASS, cotele).
///
/// Implicit vin din fișierele JSON incluse în asamblare (<c>Parameters/tax-YYYY.json</c>). Peste
/// ele, adminul poate pune valori proprii din „Privire de ansamblu" — plafoanele se schimbă prin
/// hotărâre de guvern în timpul anului, iar un deploy pentru un număr ar întârzia estimările.
/// Valorile adminului stau în <c>app_settings</c> și se încarcă aici la pornire și periodic
/// (<see cref="ApplyOverrides"/>), deci citirea rămâne sincronă și fără bază de date.
///
/// Un an fără fișier și fără valori din admin nu are parametri: motorul întoarce
/// <c>RULE_UNAVAILABLE</c>, nu refolosește pragurile altui an.
/// </summary>
public sealed class TaxYearParametersProvider
{
    /// <summary>Prefixul cheilor din <c>app_settings</c>: <c>fiscal.tax-parameters.2026</c>.</summary>
    public const string SettingKeyPrefix = "fiscal.tax-parameters.";

    private readonly Dictionary<int, TaxYearParameters> _defaults;
    private readonly ConcurrentDictionary<int, TaxYearParameters> _overrides = new();

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
        _defaults = parameters.ToDictionary(p => p.TaxYear);
    }

    /// <summary>Parametrii folosiți în calcul: cei din admin, dacă există, altfel cei din fișier.</summary>
    public TaxYearParameters? For(int taxYear) =>
        _overrides.TryGetValue(taxYear, out TaxYearParameters? custom) ? custom : _defaults.GetValueOrDefault(taxYear);

    /// <summary>Valorile din fișier, fără ce a schimbat adminul — pentru „Revino la valorile implicite".</summary>
    public TaxYearParameters? DefaultFor(int taxYear) => _defaults.GetValueOrDefault(taxYear);

    public bool IsOverridden(int taxYear) => _overrides.ContainsKey(taxYear);

    public static string SettingKey(int taxYear) => $"{SettingKeyPrefix}{taxYear}";

    /// <summary>
    /// Înlocuiește toate valorile din admin cu setul dat. Un an care lipsește din set revine la
    /// fișier — așa se propagă și o resetare făcută pe altă instanță.
    /// </summary>
    public void ApplyOverrides(IReadOnlyDictionary<int, TaxYearParameters> overrides)
    {
        foreach (int year in _overrides.Keys.Where(y => !overrides.ContainsKey(y)))
        {
            _overrides.TryRemove(year, out _);
        }

        foreach ((int year, TaxYearParameters parameters) in overrides)
        {
            _overrides[year] = parameters;
        }
    }

    public void SetOverride(TaxYearParameters parameters) => _overrides[parameters.TaxYear] = parameters;

    public void RemoveOverride(int taxYear) => _overrides.TryRemove(taxYear, out _);

    public static TaxYearParameters? Deserialize(string json) =>
        JsonSerializer.Deserialize<TaxYearParameters>(json);

    public static string Serialize(TaxYearParameters parameters) => JsonSerializer.Serialize(parameters);

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
