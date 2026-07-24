namespace Domain.PfaRegistrations;

/// <summary>Rezultatul evaluării eligibilității: statusul + motivele (pentru afișare/audit).</summary>
public sealed record EligibilityEvaluation(EligibilityStatus Status, IReadOnlyList<string> Reasons);

/// <summary>
/// Regulile de eligibilitate din specificație, ca funcție pură (fără efecte laterale, ușor de testat):
/// vârstă minimă 21 de ani, categoria B deținută de minimum 2 ani, permis nevalabil expirat,
/// atestat de transport alternativ prezent și nevalabil expirat.
/// Modelul NU primește CNP — data nașterii vine deja derivată în amonte.
/// </summary>
public static class EligibilityRules
{
    public const int MinAgeYears = 21;
    public const int MinCategoryBYears = 2;

    public static EligibilityEvaluation Evaluate(
        DateOnly? dateOfBirth,
        DateOnly? categoryBObtainedOn,
        DateOnly? drivingLicenceExpiresOn,
        bool hasDriverCertificate,
        DateOnly? driverCertificateExpiresOn,
        DateOnly today)
    {
        var hardFailures = new List<string>();
        var missing = new List<string>();

        if (dateOfBirth is null)
        {
            missing.Add("Data nașterii lipsește din documente.");
        }
        else if (YearsBetween(dateOfBirth.Value, today) < MinAgeYears)
        {
            hardFailures.Add($"Vârsta minimă este {MinAgeYears} de ani.");
        }

        if (categoryBObtainedOn is null)
        {
            missing.Add("Data obținerii categoriei B lipsește.");
        }
        else if (YearsBetween(categoryBObtainedOn.Value, today) < MinCategoryBYears)
        {
            hardFailures.Add($"Categoria B trebuie deținută de minimum {MinCategoryBYears} ani.");
        }

        if (drivingLicenceExpiresOn is not null && drivingLicenceExpiresOn.Value < today)
        {
            hardFailures.Add("Permisul de conducere este expirat.");
        }

        if (!hasDriverCertificate)
        {
            hardFailures.Add("Atestatul de transport alternativ este obligatoriu.");
        }
        else if (driverCertificateExpiresOn is not null && driverCertificateExpiresOn.Value < today)
        {
            hardFailures.Add("Atestatul de transport alternativ este expirat.");
        }

        if (hardFailures.Count > 0)
        {
            return new EligibilityEvaluation(EligibilityStatus.Ineligible, hardFailures);
        }

        if (missing.Count > 0)
        {
            return new EligibilityEvaluation(EligibilityStatus.NeedsReview, missing);
        }

        return new EligibilityEvaluation(EligibilityStatus.Eligible, []);
    }

    private static int YearsBetween(DateOnly from, DateOnly to)
    {
        int years = to.Year - from.Year;
        if (to < from.AddYears(years))
        {
            years--;
        }

        return years;
    }
}
