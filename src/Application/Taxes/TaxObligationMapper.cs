using System.Globalization;
using Domain.Taxes;

namespace Application.Taxes;

/// <summary>
/// Etichetele în română și starea temporală, decise într-un singur loc. Clientul și contabila
/// văd aceleași cuvinte pentru aceeași obligație.
/// </summary>
internal static class TaxObligationMapper
{
    private static readonly CultureInfo Romanian = CultureInfo.GetCultureInfo("ro-RO");

    public static string TypeLabel(TaxObligationType type) => type switch
    {
        TaxObligationType.TvaIntracomunitar => "TVA intracomunitar",
        TaxObligationType.TaxaNerezident => "Taxă de nerezident",
        _ => "Altă obligație",
    };

    public static string StatusLabel(TaxObligationStatus status) => status switch
    {
        TaxObligationStatus.InPregatire => "În pregătire",
        TaxObligationStatus.Depusa => "Depusă",
        TaxObligationStatus.DePlata => "De plată",
        _ => "Plătită",
    };

    public static string PeriodLabel(int year, int month)
    {
        string name = Romanian.DateTimeFormat.MonthNames[Math.Clamp(month, 1, 12) - 1];
        return $"{char.ToUpper(name[0], Romanian)}{name[1..]} {year.ToString(CultureInfo.InvariantCulture)}";
    }

    public static TaxObligationResponse ToResponse(TaxObligation obligation, DateOnly today)
    {
        bool isOverdue = obligation.IsOverdue(today);

        // O obligație plătită nu mai are o numărătoare inversă de arătat.
        int? daysUntilDue = obligation.Status == TaxObligationStatus.Platita
            ? null
            : obligation.DueDate.DayNumber - today.DayNumber;

        return new TaxObligationResponse(
            obligation.Id,
            obligation.Type.ToString(),
            TypeLabel(obligation.Type),
            obligation.PeriodYear,
            obligation.PeriodMonth,
            PeriodLabel(obligation.PeriodYear, obligation.PeriodMonth),
            obligation.AmountDue,
            obligation.DueDate,
            obligation.Status.ToString(),
            StatusLabel(obligation.Status),
            isOverdue,
            daysUntilDue,
            obligation.DocumentId,
            obligation.Note,
            obligation.UpdatedAtUtc);
    }
}
