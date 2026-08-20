using System.Globalization;
using Microsoft.Extensions.Options;

namespace Application.Cars.Scoring;

/// <summary>
/// Scorul „Recomandate" al unui anunț (spec §5.2).
/// </summary>
/// <remarks>
/// Pur: aceleași intrări dau același rezultat, fără ceas propriu și fără bază de date. De aceea
/// <c>nowUtc</c> vine ca parametru — altfel testul de prospețime ar fi depins de ziua în care
/// rulează.
///
/// Scorul se **stochează** pe anunț și se recalculează la evenimente, nu la fiecare cerere:
/// sortarea unui marketplace nu are voie să depindă de cât durează un calcul per rând.
/// </remarks>
internal sealed class RecommendationScoreCalculator(IOptions<RecommendationScoringOptions> options)
    : IRecommendationScoreCalculator
{
    private readonly RecommendationScoringOptions _options = options.Value;

    public ListingScoreResult Calculate(ListingScoreInput input, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(input);

        var suggestions = new List<ScoreSuggestion>();
        int points = 0;

        points += ScoreDescription(input.Description, suggestions);
        points += ScorePhotos(input.PhotoCount, suggestions);

        if (input.DiscountActive)
        {
            points += _options.DiscountActive;
        }
        else
        {
            Suggest(suggestions, "discount", "Setează un preț redus", _options.DiscountActive);
        }

        if (input.IsAvailable)
        {
            points += _options.AvailableNow;
        }

        // Verificarea nu e o acțiune a proprietarului — o acordă RIDElance — deci nu apare ca
        // sugestie. O listă de „ce poți face" în care un rând nu e făcut de tine e o frustrare.
        if (input.OwnerVerified)
        {
            points += _options.OwnerVerified;
        }

        if (input.OwnerHasLogo)
        {
            points += _options.OwnerHasLogo;
        }
        else
        {
            Suggest(suggestions, "logo", "Încarcă logo-ul firmei", _options.OwnerHasLogo);
        }

        double multiplier = FreshnessMultiplier(input.UpdatedAtUtc, nowUtc);

        // Rotunjire la întreg: scorul e o poziție în listă, nu o măsurătoare.
        int score = (int)Math.Round(points * multiplier, MidpointRounding.AwayFromZero);

        return new ListingScoreResult(
            Math.Clamp(score, 0, 100),
            suggestions.OrderByDescending(s => s.Points).ToList());
    }

    private int ScoreDescription(string? description, List<ScoreSuggestion> suggestions)
    {
        int length = MeaningfulLength(description);

        if (length >= _options.DescriptionFullMinLength)
        {
            return _options.DescriptionFull;
        }

        if (length >= _options.DescriptionPartialMinLength)
        {
            Suggest(
                suggestions,
                "description",
                $"Extinde descrierea la cel puțin {_options.DescriptionFullMinLength} de caractere",
                _options.DescriptionFull - _options.DescriptionPartial);

            return _options.DescriptionPartial;
        }

        Suggest(
            suggestions,
            "description",
            $"Scrie o descriere de cel puțin {_options.DescriptionFullMinLength} de caractere",
            _options.DescriptionFull);

        return 0;
    }

    private int ScorePhotos(int photoCount, List<ScoreSuggestion> suggestions)
    {
        if (photoCount >= _options.PhotosManyMin)
        {
            return _options.PhotosMany;
        }

        int missing = _options.PhotosManyMin - photoCount;

        if (photoCount >= _options.PhotosFewMin)
        {
            Suggest(
                suggestions,
                "photos",
                FormatPhotoSuggestion(missing),
                _options.PhotosMany - _options.PhotosFew);

            return _options.PhotosFew;
        }

        Suggest(suggestions, "photos", FormatPhotoSuggestion(missing), _options.PhotosMany);
        return 0;
    }

    private static string FormatPhotoSuggestion(int missing) =>
        missing == 1
            ? "Adaugă încă o poză"
            : string.Create(CultureInfo.InvariantCulture, $"Adaugă {missing} poze");

    private double FreshnessMultiplier(DateTime updatedAtUtc, DateTime nowUtc)
    {
        double days = (nowUtc - updatedAtUtc).TotalDays;

        if (days <= _options.FreshnessRecentDays)
        {
            return _options.FreshnessRecent;
        }

        return days <= _options.FreshnessStaleDays ? _options.FreshnessStale : _options.FreshnessOld;
    }

    /// <summary>
    /// Lungimea „reală" a descrierii, pentru anti-abuz (spec §5.2).
    /// </summary>
    /// <remarks>
    /// Spațiile nu se numără, iar un text compus dintr-un singur caracter repetat („aaaa…") sau
    /// din prea puține caractere distincte valorează zero. Pragul de 200 e altfel trivial de atins
    /// ținând apăsată o tastă.
    /// </remarks>
    private static int MeaningfulLength(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return 0;
        }

        string compact = new(description.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (compact.Length == 0)
        {
            return 0;
        }

        int distinct = compact.Distinct().Count();

        // Sub cinci caractere distincte nu există text natural, dar există „ababab…".
        return distinct < 5 ? 0 : compact.Length;
    }

    private static void Suggest(List<ScoreSuggestion> suggestions, string id, string label, int points)
    {
        if (points > 0)
        {
            suggestions.Add(new ScoreSuggestion(id, label, points));
        }
    }
}
