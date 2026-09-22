using Application.FiscalProfiles;

namespace Application.FiscalEstimates;

/// <summary>Răspunsurile din profilul fiscal → flagurile motorului (spec §5).</summary>
public static class ProfileFlagsMapper
{
    public static ProfileFlags Map(FiscalProfileAnswers answers, int taxYear, bool pendingCorrection)
    {
        ArgumentNullException.ThrowIfNull(answers);

        var yearStart = new DateOnly(taxYear, 1, 1);
        bool pensioner = answers.Pensioner == FiscalProfileSchema.Yes;
        // Pensionar fără dată: nu știm dacă a fost tot anul, deci îl tratăm ca pe cel devenit în
        // cursul anului — de clarificat, nu presupus.
        bool pensionerFullYear = pensioner && answers.PensionerSince is DateOnly since && since < yearStart;
        bool employed = answers.Employment is "full" or "part";
        bool voluntary = answers.CasVoluntary == FiscalProfileSchema.Yes && answers.CasVoluntaryBase is > 0;

        return new ProfileFlags
        {
            PensionerFullYear = pensionerFullYear,
            PensionerMidYear = pensioner && !pensionerFullYear,
            OwnPensionSystem = answers.OwnPensionSystem == FiscalProfileSchema.Yes,
            SalariedCassExempt = employed && answers.SalaryAboveCassMin == FiscalProfileSchema.Yes,
            SalariedCassUnknown = employed && answers.SalaryAboveCassMin is null,
            StudentCassExempt = answers.Student == FiscalProfileSchema.Yes,
            OtherIncome = answers.OtherIncome == FiscalProfileSchema.Yes,
            OtherIndependent = answers.OtherIndependent == FiscalProfileSchema.Yes,
            CasVoluntaryBase = voluntary ? answers.CasVoluntaryBase : null,
            CassOptIn = answers.CassOptIn == FiscalProfileSchema.Yes,
            CarriedLosses = answers.CarriedLosses == FiscalProfileSchema.Yes,
            CrossBorder = answers.CrossBorder == FiscalProfileSchema.Yes,
            TaxPaymentsMade = answers.TaxPaymentsMade == FiscalProfileSchema.Yes,
            PendingCorrection = pendingCorrection,
        };
    }
}
