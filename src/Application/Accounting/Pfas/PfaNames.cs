namespace Application.Accounting.Pfas;

/// <summary>
/// Denumirea PFA-ului în declarații (<c>den</c>), registre și listele contabilității.
/// <para>
/// Denumirea oficială vine din certificatul de înregistrare (<c>LegalName</c>, citit din document).
/// Când lipsește — certificat necitit sau încărcat înainte de citire —, denumirea se formează ca la
/// ONRC, din numele titularului: „NUME PRENUME PFA”. Înainte, un <c>LegalName</c> gol lăsa
/// <c>den=""</c> în XML și validarea RIDElance oprea orice declarație.
/// </para>
/// </summary>
public static class PfaNames
{
    public static string Of(string? legalName, string? holderName, string? fullName, string? firstName, string? lastName)
    {
        if (!string.IsNullOrWhiteSpace(legalName))
        {
            return legalName.Trim();
        }

        string person = FirstFilled(holderName, fullName, $"{lastName} {firstName}");
        if (person.Length == 0)
        {
            return string.Empty;
        }

        return person.EndsWith(" PFA", StringComparison.OrdinalIgnoreCase) ? person : $"{person} PFA";
    }

    private static string FirstFilled(params string?[] values) =>
        values.Select(value => string.Join(' ', (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .FirstOrDefault(value => value.Length > 0) ?? string.Empty;
}
