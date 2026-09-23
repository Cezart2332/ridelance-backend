using System.Globalization;

namespace Application.FiscalProfiles;

/// <summary>
/// Schema formularului: ce întrebări există, când apar și când sunt obligatorii. Frontendul
/// are aceeași schemă (<c>src/shared/fiscal-profile/schema.ts</c>), dar sursa de adevăr e aici.
/// </summary>
public static class FiscalProfileSchema
{
    public const string Yes = "yes";
    public const string No = "no";
    public const string Unknown = "unknown";

    public const string ChooseAnswer = "Alege un răspuns.";
    public const string FillDate = "Completează data.";
    public const string FillText = "Completează câmpul.";
    public const string FillNumber = "Completează suma.";
    public const string UnknownOption = "Răspunsul nu e o opțiune validă.";

    private static readonly string[] YesNo = [Yes, No];

    public enum QuestionKind
    {
        SingleChoice,
        Date,
        Text,
        Number,
    }

    public sealed record Question(
        string Key,
        int Step,
        QuestionKind Kind,
        IReadOnlyList<string> Options,
        Func<FiscalProfileAnswers, FiscalProfileConditions, bool> Visible,
        bool Required,
        Func<FiscalProfileAnswers, object?> Read);

    private static bool Always(FiscalProfileAnswers answers, FiscalProfileConditions conditions) => true;

    public static readonly IReadOnlyList<Question> Questions =
    [
        new("dataCorrect", 1, QuestionKind.SingleChoice, YesNo, Always, true, a => a.DataCorrect),
        new("correctionDetails", 1, QuestionKind.Text, [], (a, _) => a.DataCorrect == No, true, a => a.CorrectionDetails),
        new("priorDocs", 1, QuestionKind.SingleChoice, ["have", "will_get", "none"], (_, c) => c.AskPriorDocs, true, a => a.PriorDocs),
        new("priorDocsLocation", 1, QuestionKind.SingleChoice, ["me", "prev_accountant"],
            (a, c) => c.AskPriorDocs && a.PriorDocs is "have" or "will_get", true, a => a.PriorDocsLocation),

        new("employment", 2, QuestionKind.SingleChoice, ["none", "full", "part"], Always, true, a => a.Employment),
        new("employmentStart", 2, QuestionKind.Date, [], (a, _) => a.Employment is "full" or "part", true, a => a.EmploymentStart),
        new("employmentEnd", 2, QuestionKind.Date, [], (a, _) => a.Employment is "full" or "part", false, a => a.EmploymentEnd),
        new("salaryAboveCassMin", 2, QuestionKind.SingleChoice, YesNo, (a, _) => a.Employment is "full" or "part", true, a => a.SalaryAboveCassMin),
        new("pensioner", 2, QuestionKind.SingleChoice, YesNo, Always, true, a => a.Pensioner),
        new("pensionerSince", 2, QuestionKind.Date, [], (a, _) => a.Pensioner == Yes, true, a => a.PensionerSince),
        new("student", 2, QuestionKind.SingleChoice, YesNo, Always, true, a => a.Student),
        new("ownPensionSystem", 2, QuestionKind.SingleChoice, YesNo, Always, true, a => a.OwnPensionSystem),
        new("privateContact", 2, QuestionKind.SingleChoice, YesNo, Always, true, a => a.PrivateContact),

        new("otherIndependent", 3, QuestionKind.SingleChoice, YesNo, Always, true, a => a.OtherIndependent),
        new("otherIndependentRecords", 3, QuestionKind.SingleChoice, YesNo, (a, _) => a.OtherIndependent == Yes, true, a => a.OtherIndependentRecords),

        // Sumele de mai jos sunt opționale: PFA-ul poate să nu le știe, iar atunci le completează contabilul.
        new("otherIndependentNetAnnual", 3, QuestionKind.Number, [], (a, _) => a.OtherIndependent == Yes, false, a => a.OtherIndependentNetAnnual),
        new("otherIncome", 3, QuestionKind.SingleChoice, YesNo, Always, true, a => a.OtherIncome),
        new("otherIncomeCassInsured", 3, QuestionKind.SingleChoice, [Yes, No, Unknown], (a, _) => a.OtherIncome == Yes, true, a => a.OtherIncomeCassInsured),
        new("taxPaymentsMade", 3, QuestionKind.SingleChoice, YesNo, Always, true, a => a.TaxPaymentsMade),
        new("carriedLosses", 3, QuestionKind.SingleChoice, YesNo, (_, c) => c.AskCarriedLosses, true, a => a.CarriedLosses),
        new("carriedLossesAmount", 3, QuestionKind.Number, [], (a, c) => c.AskCarriedLosses && a.CarriedLosses == Yes, false, a => a.CarriedLossesAmount),
        new("cassOptIn", 3, QuestionKind.SingleChoice, YesNo, Always, true, a => a.CassOptIn),
        new("cassOptInBase", 3, QuestionKind.Number, [], (a, _) => a.CassOptIn == Yes, false, a => a.CassOptInBase),
        new("casVoluntary", 3, QuestionKind.SingleChoice, YesNo, Always, true, a => a.CasVoluntary),
        new("casVoluntaryBase", 3, QuestionKind.Number, [], (a, _) => a.CasVoluntary == Yes, true, a => a.CasVoluntaryBase),
        new("crossBorder", 3, QuestionKind.SingleChoice, YesNo, Always, true, a => a.CrossBorder),
        new("notes", 3, QuestionKind.Text, [], Always, false, a => a.Notes),
    ];

    /// <summary>
    /// Curăță răspunsurile: textele se taie la margini, iar câmpurile condiționate ascunse se
    /// șterg. Un răspuns rămas de la o variantă abandonată nu mai contează nicăieri.
    /// </summary>
    public static FiscalProfileAnswers Normalize(FiscalProfileAnswers answers, FiscalProfileConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(conditions);

        FiscalProfileAnswers a = answers with
        {
            DataCorrect = Clean(answers.DataCorrect),
            CorrectionDetails = Clean(answers.CorrectionDetails),
            PriorDocs = Clean(answers.PriorDocs),
            PriorDocsLocation = Clean(answers.PriorDocsLocation),
            Employment = Clean(answers.Employment),
            Pensioner = Clean(answers.Pensioner),
            Student = Clean(answers.Student),
            OwnPensionSystem = Clean(answers.OwnPensionSystem),
            PrivateContact = Clean(answers.PrivateContact),
            OtherIndependent = Clean(answers.OtherIndependent),
            OtherIndependentRecords = Clean(answers.OtherIndependentRecords),
            OtherIncome = Clean(answers.OtherIncome),
            OtherIncomeCassInsured = Clean(answers.OtherIncomeCassInsured),
            TaxPaymentsMade = Clean(answers.TaxPaymentsMade),
            CarriedLosses = Clean(answers.CarriedLosses),
            CassOptIn = Clean(answers.CassOptIn),
            SalaryAboveCassMin = Clean(answers.SalaryAboveCassMin),
            CasVoluntary = Clean(answers.CasVoluntary),
            CrossBorder = Clean(answers.CrossBorder),
            Notes = Clean(answers.Notes),
        };

        // Ordinea contează: `priorDocsLocation` depinde de `priorDocs`, care poate dispărea primul.
        a = a with
        {
            CorrectionDetails = IsVisible("correctionDetails", a, conditions) ? a.CorrectionDetails : null,
            PriorDocs = IsVisible("priorDocs", a, conditions) ? a.PriorDocs : null,
        };

        a = a with
        {
            PriorDocsLocation = IsVisible("priorDocsLocation", a, conditions) ? a.PriorDocsLocation : null,
            EmploymentStart = IsVisible("employmentStart", a, conditions) ? a.EmploymentStart : null,
            EmploymentEnd = IsVisible("employmentEnd", a, conditions) ? a.EmploymentEnd : null,
            SalaryAboveCassMin = IsVisible("salaryAboveCassMin", a, conditions) ? a.SalaryAboveCassMin : null,
            CasVoluntaryBase = IsVisible("casVoluntaryBase", a, conditions) ? a.CasVoluntaryBase : null,
            PensionerSince = IsVisible("pensionerSince", a, conditions) ? a.PensionerSince : null,
            OtherIndependentRecords = IsVisible("otherIndependentRecords", a, conditions) ? a.OtherIndependentRecords : null,
            CarriedLosses = IsVisible("carriedLosses", a, conditions) ? a.CarriedLosses : null,
            OtherIndependentNetAnnual = IsVisible("otherIndependentNetAnnual", a, conditions) ? a.OtherIndependentNetAnnual : null,
            OtherIncomeCassInsured = IsVisible("otherIncomeCassInsured", a, conditions) ? a.OtherIncomeCassInsured : null,
            CassOptInBase = IsVisible("cassOptInBase", a, conditions) ? a.CassOptInBase : null,
        };

        // Suma pierderilor depinde de `carriedLosses`, care poate dispărea mai sus.
        return a with
        {
            CarriedLossesAmount = IsVisible("carriedLossesAmount", a, conditions) ? a.CarriedLossesAmount : null,
        };
    }

    /// <summary>
    /// Erorile formularului, pe cheie. <paramref name="requireAll"/> = fals pentru o ciornă: se
    /// verifică doar că răspunsurile date sunt opțiuni valide, nu că toate există.
    /// </summary>
    public static Dictionary<string, string> Validate(
        FiscalProfileAnswers answers,
        FiscalProfileConditions conditions,
        bool requireAll)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(conditions);

        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Question question in Questions)
        {
            if (!question.Visible(answers, conditions))
            {
                continue;
            }

            object? value = question.Read(answers);
            bool empty = value is null || value is string text && string.IsNullOrWhiteSpace(text);

            if (empty)
            {
                if (requireAll && question.Required)
                {
                    errors[question.Key] = question.Kind switch
                    {
                        QuestionKind.SingleChoice => ChooseAnswer,
                        QuestionKind.Date => FillDate,
                        QuestionKind.Number => FillNumber,
                        _ => FillText,
                    };
                }

                continue;
            }

            if (question.Kind == QuestionKind.SingleChoice && !question.Options.Contains((string)value!, StringComparer.Ordinal))
            {
                errors[question.Key] = UnknownOption;
            }
        }

        if (answers.EmploymentStart is DateOnly start
            && answers.EmploymentEnd is DateOnly end
            && end < start
            && !errors.ContainsKey("employmentEnd"))
        {
            errors["employmentEnd"] = "Data încetării nu poate fi înainte de data angajării.";
        }

        if (answers.CasVoluntaryBase is decimal casBase && (casBase <= 0 || casBase > 10_000_000) && !errors.ContainsKey("casVoluntaryBase"))
        {
            errors["casVoluntaryBase"] = "Suma trebuie să fie mai mare decât 0.";
        }

        // Netul altor activități poate fi 0 (un an fără câștig); pierderea și baza CASS, nu.
        if (answers.OtherIndependentNetAnnual is decimal otherNet && (otherNet < 0 || otherNet > 10_000_000)
            && !errors.ContainsKey("otherIndependentNetAnnual"))
        {
            errors["otherIndependentNetAnnual"] = "Suma nu poate fi negativă. Dacă ai avut pierdere, scrie 0.";
        }

        if (answers.CarriedLossesAmount is decimal losses && (losses <= 0 || losses > 10_000_000) && !errors.ContainsKey("carriedLossesAmount"))
        {
            errors["carriedLossesAmount"] = "Suma trebuie să fie mai mare decât 0.";
        }

        if (answers.CassOptInBase is decimal cassBase && (cassBase <= 0 || cassBase > 10_000_000) && !errors.ContainsKey("cassOptInBase"))
        {
            errors["cassOptInBase"] = "Suma trebuie să fie mai mare decât 0.";
        }

        if (answers.CorrectionDetails?.Length > 2000)
        {
            errors["correctionDetails"] = "Textul e prea lung (maximum 2000 de caractere).";
        }

        if (answers.Notes?.Length > 2000)
        {
            errors["notes"] = "Textul e prea lung (maximum 2000 de caractere).";
        }

        return errors;
    }

    /// <summary>Câmpurile care diferă între două versiuni, pentru istoric.</summary>
    public static List<FiscalProfileFieldChange> Diff(FiscalProfileAnswers before, FiscalProfileAnswers after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var changes = new List<FiscalProfileFieldChange>();
        foreach (Question question in Questions)
        {
            string? oldValue = Format(question.Read(before));
            string? newValue = Format(question.Read(after));
            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                changes.Add(new FiscalProfileFieldChange(question.Key, oldValue, newValue));
            }
        }

        return changes;
    }

    private static bool IsVisible(string key, FiscalProfileAnswers answers, FiscalProfileConditions conditions) =>
        Questions.First(q => q.Key == key).Visible(answers, conditions);

    private static string? Format(object? value) => value switch
    {
        null => null,
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        string text => text,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
