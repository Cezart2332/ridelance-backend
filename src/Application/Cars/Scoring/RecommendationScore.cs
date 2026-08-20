namespace Application.Cars.Scoring;

/// <summary>
/// Tot ce influențează scorul unui anunț, ca date simple.
/// </summary>
/// <remarks>
/// Deliberat fără tipuri de EF: calculatorul trebuie să poată fi testat cu o linie de cod, nu cu
/// o bază de date. Apelantul face proiecția.
/// </remarks>
public sealed record ListingScoreInput(
    string? Description,
    int PhotoCount,
    bool DiscountActive,
    bool IsAvailable,
    bool OwnerVerified,
    bool OwnerHasLogo,
    DateTime UpdatedAtUtc);

/// <summary>O acțiune concretă și câte puncte aduce: „Adaugă 3 poze: +7".</summary>
public sealed record ScoreSuggestion(string Id, string Label, int Points);

public sealed record ListingScoreResult(int Score, IReadOnlyList<ScoreSuggestion> Suggestions);

public interface IRecommendationScoreCalculator
{
    ListingScoreResult Calculate(ListingScoreInput input, DateTime nowUtc);
}
