namespace Application.FiscalProfiles;

/// <summary>
/// Schema profilului fiscal: trei situații, fiecare <c>yes</c> / <c>no</c>. Frontendul are aceeași
/// listă (<c>src/shared/fiscal-profile/schema.ts</c>), dar sursa de adevăr e aici.
/// </summary>
public static class FiscalProfileSchema
{
    public const string Yes = "yes";
    public const string No = "no";

    public const string ChooseAnswer = "Alege situația ta sau „Niciuna”.";
    public const string UnknownOption = "Răspunsul nu e o opțiune validă.";

    public sealed record Situation(string Key, Func<FiscalProfileAnswers, string?> Read);

    public static readonly IReadOnlyList<Situation> Situations =
    [
        new("pensioner", a => a.Pensioner),
        new("student", a => a.Student),
        new("employedFullTime", a => a.EmployedFullTime),
    ];

    /// <summary>Răspunsurile curățate la margini; un răspuns gol devine necompletat.</summary>
    public static FiscalProfileAnswers Normalize(FiscalProfileAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        return new FiscalProfileAnswers
        {
            Pensioner = Clean(answers.Pensioner),
            Student = Clean(answers.Student),
            EmployedFullTime = Clean(answers.EmployedFullTime),
        };
    }

    /// <summary>
    /// Erorile, pe cheie. <paramref name="requireAll"/> = fals pentru o ciornă: se verifică doar că
    /// răspunsurile date sunt valide, nu că toate există.
    /// </summary>
    public static Dictionary<string, string> Validate(FiscalProfileAnswers answers, bool requireAll)
    {
        ArgumentNullException.ThrowIfNull(answers);

        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Situation situation in Situations)
        {
            string? value = situation.Read(answers);
            if (value is null)
            {
                if (requireAll)
                {
                    errors[situation.Key] = ChooseAnswer;
                }

                continue;
            }

            if (value is not (Yes or No))
            {
                errors[situation.Key] = UnknownOption;
            }
        }

        return errors;
    }

    /// <summary>Câmpurile care diferă între două versiuni, pentru istoric.</summary>
    public static List<FiscalProfileFieldChange> Diff(FiscalProfileAnswers before, FiscalProfileAnswers after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        return
        [
            .. Situations
                .Select(s => new FiscalProfileFieldChange(s.Key, s.Read(before), s.Read(after)))
                .Where(change => !string.Equals(change.OldValue, change.NewValue, StringComparison.Ordinal)),
        ];
    }

    /// <summary>Eticheta scurtă a situației, pentru liste: „Pensionar · Angajat” sau „Standard”.</summary>
    public static string Label(FiscalProfileAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var parts = new List<string>();
        if (answers.Pensioner == Yes)
        {
            parts.Add("Pensionar");
        }

        if (answers.Student == Yes)
        {
            parts.Add("Student");
        }

        if (answers.EmployedFullTime == Yes)
        {
            parts.Add("Angajat");
        }

        return parts.Count == 0 ? "Standard" : string.Join(" · ", parts);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
