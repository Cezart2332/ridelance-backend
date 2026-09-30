namespace Infrastructure.Eldrive;

/// <summary>
/// Contul de partener Eldrive. Secțiunea <c>Eldrive</c> din configurație; în producție,
/// <c>Eldrive__ApiKey</c> ca variabilă de mediu.
/// </summary>
public sealed class EldriveOptions
{
    public const string SectionName = "Eldrive";

    /// <summary>Tokenul Bearer primit de la Eldrive. Fără el, conectarea eșuează explicit.</summary>
    public string? ApiKey { get; set; }

    /// <summary>ID-ul RIDElance ca partener Eldrive.</summary>
    public long PartnerId { get; set; } = 1452;

    public Uri BaseUrl { get; set; } = new("https://cp.eldrive.eu/public-api/resources/");

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
