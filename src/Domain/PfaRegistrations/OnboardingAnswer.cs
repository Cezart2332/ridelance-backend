namespace Domain.PfaRegistrations;

/// <summary>
/// Un răspuns dat în onboarding (o întrebare Da/Nu, o alegere, un câmp), așa cum l-a văzut omul.
/// Se leagă de user, nu de dosarul PFA: eligibilitatea vine înaintea dosarului.
/// </summary>
/// <remarks>
/// Rândurile nu se modifică: fiecare schimbare de răspuns e un rând nou, ca adminul să vadă și
/// că cineva a apăsat întâi „Nu” și apoi „Da”. Textul întrebării și al răspunsului se păstrează
/// cum erau pe ecran în momentul răspunsului — formulările din aplicație se pot schimba.
/// Parolele nu ajung niciodată aici.
/// </remarks>
public sealed class OnboardingAnswer
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Pasul mare: <c>eligibility</c>, <c>pfa</c>, <c>fiscal</c>, <c>platforms</c>...</summary>
    public string StepKey { get; set; } = string.Empty;

    /// <summary>Micro-pasul (sau câmpul lui): <c>age</c>, <c>tva</c>, <c>pfa_contact.phone</c>...</summary>
    public string QuestionId { get; set; } = string.Empty;

    public string Question { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ValueLabel { get; set; } = string.Empty;
    public DateTime AnsweredAtUtc { get; set; } = DateTime.UtcNow;
}

public static class OnboardingAnswerLimits
{
    public const int Question = 300;
    public const int Value = 2000;
}
