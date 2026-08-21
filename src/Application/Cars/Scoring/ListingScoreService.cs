using Application.Abstractions.Data;
using Domain.Cars;
using Domain.Companies;
using Microsoft.EntityFrameworkCore;

namespace Application.Cars.Scoring;

/// <summary>
/// Leagă calculatorul pur de baza de date: citește ce îi trebuie, recalculează, salvează.
/// </summary>
/// <remarks>
/// Există ca serviciu separat pentru că punctele de recalculare sunt multe și împrăștiate —
/// creare și editare de anunț, poze adăugate sau șterse, schimbare de disponibilitate, logo nou,
/// verificare acordată (spec §7.3). Dacă fiecare și-ar fi făcut propria proiecție, ar fi fost
/// doar o chestiune de timp până când două dintre ele ar fi calculat scoruri diferite pentru
/// același anunț.
///
/// Nu apelează `SaveChangesAsync`: intră în aceeași tranzacție cu operațiunea care l-a
/// declanșat, deci un anunț salvat are întotdeauna un scor salvat odată cu el.
/// </remarks>
public sealed class ListingScoreService(
    IApplicationDbContext context,
    IRecommendationScoreCalculator calculator)
{
    /// <summary>Recalculează scorul unui anunț. Anunțul trebuie să fie urmărit de context.</summary>
    public async Task RecalculateAsync(Car car, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(car);

        ListingScoreInput input = await BuildInputAsync(car, cancellationToken);
        Apply(car, input);
    }

    /// <summary>Recalculează după Id, când apelantul nu are deja entitatea.</summary>
    public async Task RecalculateAsync(Guid carId, CancellationToken cancellationToken)
    {
        Car? car = await context.Cars
            .Include(c => c.Images)
            .FirstOrDefaultAsync(c => c.Id == carId, cancellationToken);

        if (car is not null)
        {
            await RecalculateAsync(car, cancellationToken);
        }
    }

    /// <summary>
    /// Toate anunțurile unui proprietar. Se apelează când se schimbă ceva despre el, nu despre
    /// mașini: logo încărcat, verificare acordată.
    /// </summary>
    public async Task RecalculateForOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        List<Car> cars = await context.Cars
            .Include(c => c.Images)
            .Where(c => c.PostedByUserId == ownerUserId)
            .ToListAsync(cancellationToken);

        if (cars.Count == 0)
        {
            return;
        }

        CompanyProfile? owner = await LoadOwnerAsync(ownerUserId, cancellationToken);

        foreach (Car car in cars)
        {
            Apply(car, BuildInput(car, owner));
        }
    }

    private void Apply(Car car, ListingScoreInput input)
    {
        DateTime now = DateTime.UtcNow;
        ListingScoreResult result = calculator.Calculate(input, now);

        car.RecommendationScore = result.Score;
        car.ScoreComputedAtUtc = now;
    }

    private async Task<ListingScoreInput> BuildInputAsync(Car car, CancellationToken cancellationToken)
    {
        CompanyProfile? owner = car.PostedByUserId.HasValue
            ? await LoadOwnerAsync(car.PostedByUserId.Value, cancellationToken)
            : null;

        return BuildInput(car, owner);
    }

    private static ListingScoreInput BuildInput(Car car, CompanyProfile? owner) => new(
        car.Description,
        car.Images.Count,
        // O reducere „activă" fără preț vechi nu e o reducere, e un bifat rămas în urmă.
        car.DiscountActive && car.OldPrice.HasValue && car.OldPrice > car.PricePerWeek,
        car.Status == CarStatus.Available,
        owner?.IsVerified ?? false,
        !string.IsNullOrWhiteSpace(owner?.LogoUrl),
        car.UpdatedAtUtc);

    private Task<CompanyProfile?> LoadOwnerAsync(Guid userId, CancellationToken cancellationToken) =>
        context.CompanyProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    /// <summary>
    /// Sugestiile pentru un anunț, pentru indicatorul din dashboardul proprietarului (§5.2).
    /// Nu se stochează: sunt derivate din aceleași date ca scorul, iar o copie stocată ar putea
    /// rămâne în urmă față de el.
    /// </summary>
    public async Task<IReadOnlyList<ScoreSuggestion>> GetSuggestionsAsync(
        Car car,
        CancellationToken cancellationToken)
    {
        ListingScoreInput input = await BuildInputAsync(car, cancellationToken);
        return calculator.Calculate(input, DateTime.UtcNow).Suggestions;
    }
}
