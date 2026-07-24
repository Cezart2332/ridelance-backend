namespace Domain.PfaRegistrations;

/// <summary>Rezultatul verificării de eligibilitate (Pasul 0 din onboarding).</summary>
public enum EligibilityStatus
{
    /// <summary>Încă nu s-au strâns toate datele pentru evaluare.</summary>
    Pending = 0,

    /// <summary>Îndeplinește toate condițiile.</summary>
    Eligible = 1,

    /// <summary>O condiție clară nu e îndeplinită (vârstă, vechime, atestat, expirare).</summary>
    Ineligible = 2,

    /// <summary>Date lipsă / neclare — necesită verificare manuală, nu blochează automat.</summary>
    NeedsReview = 3,
}
