using Application.Cars.Scoring;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace UnitTests.Cars;

/// <summary>
/// Scorul decide ordinea în care sunt văzute anunțurile, deci o pondere aplicată greșit nu se
/// manifestă ca o eroare, ci ca trafic mutat tăcut de la un partener la altul. Fiecare criteriu
/// din spec §5.2 are aici un test care îl izolează.
/// </summary>
public class RecommendationScoreCalculatorTests
{
    private static readonly DateTime Now = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Descriere validă, suficient de lungă și cu destule caractere distincte.</summary>
    private const string FullDescription =
        "Mașină electrică ideală pentru ridesharing în București, cu autonomie reală de peste 300 km, " +
        "scaune încălzite, cameră de marșarier și revizie făcută recent la reprezentanță. Predarea se " +
        "face în aceeași zi, cu contract și proces verbal semnate pe loc, iar asigurarea este inclusă.";

    private static RecommendationScoreCalculator Calculator(RecommendationScoringOptions? options = null) =>
        new(Options.Create(options ?? new RecommendationScoringOptions()));

    /// <summary>Anunț „gol": nimic completat, actualizat azi. Punctul de plecare al fiecărui test.</summary>
    private static ListingScoreInput Empty() => new(
        Description: null,
        PhotoCount: 0,
        DiscountActive: false,
        IsAvailable: false,
        OwnerVerified: false,
        OwnerHasLogo: false,
        UpdatedAtUtc: Now);

    [Fact]
    public void EmptyListing_ScoresZero()
    {
        Calculator().Calculate(Empty(), Now).Score.ShouldBe(0);
    }

    [Fact]
    public void FullDescription_Scores30()
    {
        FullDescription.Replace(" ", "", StringComparison.Ordinal).Length.ShouldBeGreaterThanOrEqualTo(200);

        Calculator().Calculate(Empty() with { Description = FullDescription }, Now).Score.ShouldBe(30);
    }

    [Fact]
    public void PartialDescription_Scores15()
    {
        string partial = FullDescription[..80];

        Calculator().Calculate(Empty() with { Description = partial }, Now).Score.ShouldBe(15);
    }

    [Fact]
    public void ShortDescription_ScoresNothing()
    {
        Calculator().Calculate(Empty() with { Description = "Mașină bună." }, Now).Score.ShouldBe(0);
    }

    /// <summary>
    /// Anti-abuz (spec §5.2): pragul de 200 nu se atinge ținând apăsată o tastă.
    /// </summary>
    [Fact]
    public void RepeatedCharacters_DoNotCountAsDescription()
    {
        Calculator().Calculate(Empty() with { Description = new string('a', 400) }, Now).Score.ShouldBe(0);
        Calculator().Calculate(Empty() with { Description = string.Concat(Enumerable.Repeat("ab", 200)) }, Now)
            .Score.ShouldBe(0);
    }

    [Fact]
    public void WhitespaceOnlyDescription_ScoresNothing()
    {
        Calculator().Calculate(Empty() with { Description = new string(' ', 500) }, Now).Score.ShouldBe(0);
    }

    [Fact]
    public void SixPhotos_Score15()
    {
        Calculator().Calculate(Empty() with { PhotoCount = 6 }, Now).Score.ShouldBe(15);
    }

    [Fact]
    public void ThreePhotos_Score8()
    {
        Calculator().Calculate(Empty() with { PhotoCount = 3 }, Now).Score.ShouldBe(8);
    }

    [Fact]
    public void TwoPhotos_ScoreNothing()
    {
        Calculator().Calculate(Empty() with { PhotoCount = 2 }, Now).Score.ShouldBe(0);
    }

    [Fact]
    public void ActiveDiscount_Scores20()
    {
        Calculator().Calculate(Empty() with { DiscountActive = true }, Now).Score.ShouldBe(20);
    }

    [Fact]
    public void AvailableNow_Scores5()
    {
        Calculator().Calculate(Empty() with { IsAvailable = true }, Now).Score.ShouldBe(5);
    }

    [Fact]
    public void VerifiedOwner_Scores5()
    {
        Calculator().Calculate(Empty() with { OwnerVerified = true }, Now).Score.ShouldBe(5);
    }

    [Fact]
    public void OwnerLogo_Scores5()
    {
        Calculator().Calculate(Empty() with { OwnerHasLogo = true }, Now).Score.ShouldBe(5);
    }

    [Theory]
    [InlineData(0, 80)]    // actualizat azi
    [InlineData(7, 80)]    // exact la limita „recent"
    [InlineData(8, 72)]    // 80 × 0.9
    [InlineData(30, 72)]   // ultima zi din intervalul mijlociu
    [InlineData(31, 60)]   // 80 × 0.75
    [InlineData(400, 60)]
    public void Freshness_ScalesTheWholeScore(int daysOld, int expected)
    {
        ListingScoreInput input = Perfect() with { UpdatedAtUtc = Now.AddDays(-daysOld) };

        Calculator().Calculate(input, Now).Score.ShouldBe(expected);
    }

    /// <summary>
    /// Maximul atingibil azi e 80, nu 100: pinul pe hartă și dosarul vehiculului nu au încă sursă
    /// de date, deci ponderile lor sunt zero. Testul fixează cifra ca să nu se schimbe din greșeală.
    /// </summary>
    [Fact]
    public void PerfectListing_Scores80_UntilMapAndDossierExist()
    {
        ListingScoreResult result = Calculator().Calculate(Perfect(), Now);

        result.Score.ShouldBe(80);
        result.Suggestions.ShouldBeEmpty();
    }

    [Fact]
    public void Suggestions_SayWhatToDoAndWhatItIsWorth()
    {
        ListingScoreResult result = Calculator().Calculate(Empty() with { PhotoCount = 3 }, Now);

        ScoreSuggestion photos = result.Suggestions.Single(s => s.Id == "photos");
        photos.Points.ShouldBe(7);
        photos.Label.ShouldBe("Adaugă 3 poze");
    }

    /// <summary>Verificarea nu e o acțiune a proprietarului, deci nu i se propune.</summary>
    [Fact]
    public void Verification_IsNotSuggested()
    {
        Calculator().Calculate(Empty(), Now).Suggestions.ShouldNotContain(s => s.Id == "verified");
    }

    [Fact]
    public void Suggestions_AreOrderedByWhatTheyAreWorth()
    {
        IReadOnlyList<ScoreSuggestion> suggestions = Calculator().Calculate(Empty(), Now).Suggestions;

        suggestions.Select(s => s.Points).ShouldBe(suggestions.Select(s => s.Points).OrderByDescending(p => p));
    }

    /// <summary>
    /// Determinism: același anunț dă același scor la fiecare apel. Fără asta, ordonarea
    /// `score DESC, updated_at DESC, id ASC` din §5.2 n-ar mai fi stabilă între cereri, iar la
    /// paginare rândurile ar sări.
    /// </summary>
    [Fact]
    public void Calculate_IsDeterministic()
    {
        ListingScoreInput input = Perfect() with { UpdatedAtUtc = Now.AddDays(-9) };
        RecommendationScoreCalculator calculator = Calculator();

        int first = calculator.Calculate(input, Now).Score;

        for (int i = 0; i < 20; i++)
        {
            calculator.Calculate(input, Now).Score.ShouldBe(first);
        }
    }

    [Fact]
    public void Weights_ComeFromConfiguration()
    {
        var options = new RecommendationScoringOptions { DiscountActive = 42 };

        Calculator(options).Calculate(Empty() with { DiscountActive = true }, Now).Score.ShouldBe(42);
    }

    /// <summary>
    /// Criteriile fără sursă de date se pot aprinde din configurare, fără modificare de cod.
    /// </summary>
    [Fact]
    public void MapAndDossierWeights_AreConfigurableButOffByDefault()
    {
        new RecommendationScoringOptions().MapPin.ShouldBe(0);
        new RecommendationScoringOptions().VehicleDossier.ShouldBe(0);
    }

    private static ListingScoreInput Perfect() => new(
        Description: FullDescription,
        PhotoCount: 8,
        DiscountActive: true,
        IsAvailable: true,
        OwnerVerified: true,
        OwnerHasLogo: true,
        UpdatedAtUtc: Now);
}
