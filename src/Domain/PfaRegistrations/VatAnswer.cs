namespace Domain.PfaRegistrations;

/// <summary>Răspunsul clientului la întrebarea despre TVA intracomunitar (Pasul 2.1).</summary>
public enum VatAnswer
{
    /// <summary>Clientul nu a răspuns încă — nu poate fi trimis ca declarație.</summary>
    Unknown = 0,
    Yes = 1,
    No = 2,
    /// <summary>
    /// Valoare istorică („nu știu”). Nu mai poate fi trimisă din onboarding; rămâne
    /// declarată doar ca dosarele salvate înainte să se poată citi.
    /// </summary>
    DontKnow = 3,
}
