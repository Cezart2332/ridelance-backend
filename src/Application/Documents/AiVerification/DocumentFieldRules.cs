namespace Application.Documents.AiVerification;

/// <summary>
/// Reguli pe câmpurile citite, per tip de document, care nu încap în validatoarele de format.
/// Funcții pure, ca <see cref="DocumentDateValidator"/>.
/// </summary>
public static class DocumentFieldRules
{
    private static readonly char[] ListSeparators = [',', ';', '|', '\n', '\r'];

    /// <summary>
    /// Data până la care e valabil ITP-ul, din toate datele citite în rubrica ITP a talonului: cea
    /// mai îndepărtată. Datele egale cu B (prima înmatriculare) sau I (înmatricularea) se aruncă —
    /// sunt exact confuzia pe care o reparăm. Null când nu rămâne nicio dată.
    /// </summary>
    public static DateOnly? LatestItpDate(string? allDates, DateOnly? firstRegistration, DateOnly? registration)
    {
        if (string.IsNullOrWhiteSpace(allDates))
        {
            return null;
        }

        return allDates
            .Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(DocumentDateValidator.Parse)
            .Where(d => d is not null && d != firstRegistration && d != registration)
            .Max();
    }

    /// <summary>
    /// Problema cu data obținerii categoriei B, dacă există. Data lipsă sau ulterioară emiterii
    /// permisului (4a) înseamnă că s-a citit alt rând sau altă coloană: vechimea de 2 ani nu se
    /// poate stabili din ea, deci decide un om.
    /// </summary>
    public static string? CategoryBProblem(DateOnly? categoryBObtainedOn, DateOnly? issuedOn4a)
    {
        if (categoryBObtainedOn is null)
        {
            return "Nu am putut citi data obținerii categoriei B.";
        }

        if (issuedOn4a is DateOnly issued && categoryBObtainedOn.Value > issued)
        {
            return "Data obținerii categoriei B e după data emiterii permisului.";
        }

        return null;
    }

    /// <summary>Concluzia unei adeverințe sau a unui aviz spune „inapt”.</summary>
    public static bool SaysUnfit(string? conclusion) =>
        conclusion is not null &&
        conclusion.Contains("inapt", StringComparison.OrdinalIgnoreCase);
}
