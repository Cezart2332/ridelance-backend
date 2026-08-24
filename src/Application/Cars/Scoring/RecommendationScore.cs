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
    DateTime UpdatedAtUtc,
    /// <summary>Pinul de preluare, nu doar orașul ca text.</summary>
    bool HasMapPin = false,
    /// <summary>
    /// Câte dintre cele patru câmpuri administrative sunt completate: număr, VIN, kilometraj,
    /// primă înmatriculare. Se trimite ca fracție, nu ca „e complet": pragul e o setare.
    /// </summary>
    double DossierCompletion = 0);

/// <summary>O acțiune concretă și câte puncte aduce: „Adaugă 3 poze: +7".</summary>
public sealed record ScoreSuggestion(string Id, string Label, int Points);

public sealed record ListingScoreResult(int Score, IReadOnlyList<ScoreSuggestion> Suggestions);

public interface IRecommendationScoreCalculator
{
    ListingScoreResult Calculate(ListingScoreInput input, DateTime nowUtc);
}
