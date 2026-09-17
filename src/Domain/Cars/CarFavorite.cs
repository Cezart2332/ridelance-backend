namespace Domain.Cars;

/// <summary>
/// O mașină salvată la favorite de un utilizator cu cont.
/// </summary>
/// <remarks>
/// Cine n-are cont își ține favoritele în browser; la prima sesiune ele se mută aici, prin
/// <c>MergeCarFavoritesCommand</c>. Perechea (utilizator, mașină) e unică — o mașină e sau nu e
/// la favorite, nu apare de două ori.
/// </remarks>
public sealed class CarFavorite
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CarId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public Car Car { get; set; } = null!;
}
