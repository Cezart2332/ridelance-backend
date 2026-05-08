using System.Globalization;

namespace Domain.PfaRegistrations;

public static class CuiValidator
{
    public static (bool IsValid, string Message) Validate(string? v)
    {
        if (string.IsNullOrWhiteSpace(v))
        {
            return (false, "CUI-ul nu este un șir de caractere valid!");
        }

        string cif = v.Trim().ToUpperInvariant();

        // remove RO from cif:
        if (cif.StartsWith("RO", StringComparison.Ordinal))
        {
            cif = cif.Substring(2).Trim();
        }

        // remove spaces:
        cif = cif.Replace(" ", "", StringComparison.Ordinal);

        // validate character length:
        if (cif.Length < 2 || cif.Length > 10)
        {
            return (false, "Lungimea corectă fără RO, este între 2 și 10 caractere!");
        }

        // validate that so far the resulting CIF looks like an integer value:
        if (!long.TryParse(cif, out _))
        {
            return (false, "Nu este număr!");
        }

        // begin testing:
        const string testKey = "753217532";
        int controlNumber = int.Parse(cif.AsSpan(cif.Length - 1), CultureInfo.InvariantCulture);

        // remove control number:
        cif = cif.Substring(0, cif.Length - 1);

        // pad left with zeros to reach testKey length:
        cif = cif.PadLeft(testKey.Length, '0');

        int sum = 0;
        for (int i = 0; i < cif.Length; i++)
        {
            sum += (int)char.GetNumericValue(cif[i]) * (int)char.GetNumericValue(testKey[i]);
        }

        int calculatedControlNumber = sum * 10 % 11;

        if (calculatedControlNumber == 10)
        {
            calculatedControlNumber = 0;
        }

        if (controlNumber == calculatedControlNumber)
        {
            return (true, string.Empty);
        }

        return (false, $"CIF invalid! Cifra de control calculată ({calculatedControlNumber}) diferă de cea introdusă ({controlNumber})!");
    }
}
