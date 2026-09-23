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
        bool otherIndependent = answers.OtherIndependent == FiscalProfileSchema.Yes;
        bool otherIncome = answers.OtherIncome == FiscalProfileSchema.Yes;
        bool cassOptIn = answers.CassOptIn == FiscalProfileSchema.Yes;
        bool carriedLosses = answers.CarriedLosses == FiscalProfileSchema.Yes;

        return new ProfileFlags
        {
            PensionerFullYear = pensionerFullYear,
            PensionerMidYear = pensioner && !pensionerFullYear,
            OwnPensionSystem = answers.OwnPensionSystem == FiscalProfileSchema.Yes,
            SalariedCassExempt = employed && answers.SalaryAboveCassMin == FiscalProfileSchema.Yes,
            SalariedCassUnknown = employed && answers.SalaryAboveCassMin is null,
            StudentCassExempt = answers.Student == FiscalProfileSchema.Yes,
            OtherIncome = otherIncome,
            OtherIncomeCassInsured = !otherIncome ? null : answers.OtherIncomeCassInsured switch
            {
                FiscalProfileSchema.Yes => true,
                FiscalProfileSchema.No => false,
                _ => null,
            },
            OtherIndependent = otherIndependent,
            OtherIndependentNetAnnual = otherIndependent ? answers.OtherIndependentNetAnnual : null,
            CasVoluntaryBase = voluntary ? answers.CasVoluntaryBase : null,
            CassOptIn = cassOptIn,
            CassOptInBase = cassOptIn && answers.CassOptInBase is > 0 ? answers.CassOptInBase : null,
            CarriedLosses = carriedLosses,
            CarriedLossesAmount = carriedLosses && answers.CarriedLossesAmount is > 0 ? answers.CarriedLossesAmount : null,
            CrossBorder = answers.CrossBorder == FiscalProfileSchema.Yes,
            TaxPaymentsMade = answers.TaxPaymentsMade == FiscalProfileSchema.Yes,
            PendingCorrection = pendingCorrection,
        };
    }
}
