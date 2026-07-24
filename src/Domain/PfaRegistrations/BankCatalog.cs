namespace Domain.PfaRegistrations;

/// <summary>
/// Catalogul băncilor oferite la Pasul 2.3, cu BCR primul (partenerul principal).
/// Reflectă lista din frontend — se țin sincronizate manual.
/// </summary>
public static class BankCatalog
{
    public static readonly IReadOnlyList<string> Banks =
    [
        "BCR",
        "Banca Transilvania",
        "BRD",
        "ING Bank",
        "Raiffeisen Bank",
        "UniCredit Bank",
        "CEC Bank",
        "Alpha Bank",
        "OTP Bank",
        "First Bank",
        "Libra Internet Bank",
        "Revolut",
    ];

    public static bool IsKnown(string? bankName) =>
        !string.IsNullOrWhiteSpace(bankName) &&
        Banks.Any(b => string.Equals(b, bankName, StringComparison.OrdinalIgnoreCase));
}
