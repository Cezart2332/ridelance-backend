namespace Domain.PfaRegistrations;

/// <summary>
/// Reguli deterministe pentru perioada copiei conforme și calculul taxelor (Pasul 5).
/// Funcție pură, testabilă — la fel ca <see cref="EligibilityRules"/>.
/// </summary>
public static class CopyConformaRules
{
    public const int MinYears = 1;
    /// <summary>
    /// Perioada maximă pentru care se poate solicita copia conformă: 3 ani, fără a depăși
    /// valabilitatea autorizației de transport (care se emite tot pe 3 ani).
    /// </summary>
    public const int MaxYears = 3;

    public static bool IsValidPeriod(int years) => years is >= MinYears and <= MaxYears;

    public static int ClampYears(int years) => Math.Clamp(years, MinYears, MaxYears);

    /// <summary>Total copie conformă (bani) = taxă/an × ani (ani limitați la intervalul valid).</summary>
    public static long ComputeCopyTotalBani(long feePerYearBani, int years) =>
        feePerYearBani * ClampYears(years);

    /// <summary>Total ecusoane (bani) = taxă/set × seturi (minim 1 set).</summary>
    public static long ComputeBadgesTotalBani(long feePerSetBani, int setCount) =>
        feePerSetBani * Math.Max(1, setCount);
}
