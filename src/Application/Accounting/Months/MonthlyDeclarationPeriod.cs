using Application.FiscalProfiles;

namespace Application.Accounting.Months;

internal static class MonthlyDeclarationPeriod
{
    public static bool IsCompleted(string period, DateTime utcNow) =>
        string.CompareOrdinal(period, FiscalProfileService.ToRomania(utcNow).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)) < 0;

    public const string Message = "Luna este încă în curs. Poți verifica documentele și cheltuielile; declarațiile se procesează după încheierea lunii.";
}
