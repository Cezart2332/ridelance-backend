using Application.FiscalProfiles;

namespace Application.FiscalEstimates;

/// <summary>Situația din profilul fiscal → flagurile motorului.</summary>
public static class ProfileFlagsMapper
{
    public static ProfileFlags Map(FiscalProfileAnswers answers, bool pendingCorrection)
    {
        ArgumentNullException.ThrowIfNull(answers);

        return new ProfileFlags
        {
            Pensioner = answers.Pensioner == FiscalProfileSchema.Yes,
            Student = answers.Student == FiscalProfileSchema.Yes,
            EmployedFullTime = answers.EmployedFullTime == FiscalProfileSchema.Yes,
            PendingCorrection = pendingCorrection,
        };
    }
}
