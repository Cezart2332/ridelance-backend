namespace Infrastructure.Localities;

/// <summary>
/// API-ul localitati.dev. Secțiunea <c>Localitati</c> din configurație; în producție,
/// <c>Localitati__ApiKey</c> ca variabilă de mediu. Fără cheie merge, dar doar 20 de cereri pe zi.
/// </summary>
public sealed class LocalitatiOptions
{
    public const string SectionName = "Localitati";

    public string? ApiKey { get; set; }

    public Uri BaseUrl { get; set; } = new("https://api.localitati.dev/v1/");
}
