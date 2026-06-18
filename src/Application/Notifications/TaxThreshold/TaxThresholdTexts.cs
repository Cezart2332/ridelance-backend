using System.Globalization;
using Application.PfaRegistrations;

namespace Application.Notifications.TaxThreshold;

public static class TaxThresholdTexts
{
    public const string PushTitle = "Praguri taxe PFA";

    public static string BuildNotificationText(int year, PfaTaxCalculator.TaxThresholdProgress progress)
    {
        string profit = FormatLei(progress.Profit);
        string casText = BuildCasText(progress);
        string cassText = BuildCassText(progress);

        return $"Update taxe {year}: profit estimat YTD {profit}. {casText} {cassText}";
    }

    public static string BuildPushNotificationText(int year, PfaTaxCalculator.TaxThresholdProgress progress)
    {
        string casRemaining = progress.RemainingToNextCasThreshold > 0
            ? FormatLei(progress.RemainingToNextCasThreshold)
            : "0 lei";
        string cassRemaining = progress.RemainingToNextCassThreshold > 0
            ? FormatLei(progress.RemainingToNextCassThreshold)
            : "0 lei";

        return $"Praguri {year}: CAS {casRemaining} rămas, CASS {cassRemaining} rămas.";
    }

    private static string BuildCasText(PfaTaxCalculator.TaxThresholdProgress progress)
    {
        if (!progress.HasReachedCasFirstThreshold)
        {
            return $"CAS: mai ai {FormatLei(progress.RemainingToNextCasThreshold)} până la pragul de 12 salarii ({FormatLei(progress.CasFirstThreshold)}).";
        }

        if (!progress.HasReachedCasSecondThreshold)
        {
            return $"CAS: ai trecut pragul de 12 salarii; mai ai {FormatLei(progress.RemainingToNextCasThreshold)} până la pragul de 24 salarii ({FormatLei(progress.CasSecondThreshold)}).";
        }

        return $"CAS: ai atins pragul de 24 salarii ({FormatLei(progress.CasSecondThreshold)}).";
    }

    private static string BuildCassText(PfaTaxCalculator.TaxThresholdProgress progress)
    {
        if (!progress.HasReachedCassFirstThreshold)
        {
            return $"CASS: mai ai {FormatLei(progress.RemainingToNextCassThreshold)} până la pragul de 6 salarii ({FormatLei(progress.CassFirstThreshold)}).";
        }

        if (!progress.HasReachedCassMaximumThreshold)
        {
            return $"CASS: mai ai {FormatLei(progress.RemainingToNextCassThreshold)} până la plafonul maxim de 72 salarii ({FormatLei(progress.CassMaximumThreshold)}).";
        }

        return $"CASS: ai atins plafonul maxim de 72 salarii ({FormatLei(progress.CassMaximumThreshold)}).";
    }

    private static string FormatLei(decimal value) =>
        $"{value.ToString("N0", CultureInfo.GetCultureInfo("ro-RO"))} lei";
}
