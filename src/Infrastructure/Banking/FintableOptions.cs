namespace Infrastructure.Banking;

public sealed class FintableOptions
{
    public const string SectionName = "Fintable";

    /// <summary>
    /// Personal Access Token din Dashboard → API. Se trimite ca `Authorization: Bearer`.
    ///
    /// Tokenul e al contului, nu al unui utilizator: toate conexiunile bancare ale clienților
    /// ajung în același workspace, iar separarea între ei o face exclusiv codul nostru.
    /// Se pune prin variabilă de mediu (<c>Fintable__Token</c>), niciodată în appsettings.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://fintable.io/api/v2";

    /// <summary>
    /// Workspace-ul pe care lucrăm. Gol înseamnă workspace-ul implicit al tokenului — cazul
    /// obișnuit. Se trimite mereu ca parametru de query, niciodată în body (cerința API-ului).
    /// </summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>
    /// Câte zile de istoric se cer la prima sincronizare a unui cont. Fintable nu expune o
    /// limită per bancă, așa cum făcea PSD2, deci e o singură valoare pentru toți.
    /// </summary>
    public int InitialHistoryDays { get; set; } = 365;

    /// <summary>Câte pagini de tranzacții acceptăm într-un singur apel, ca plasă contra buclelor.</summary>
    public int MaxTransactionPages { get; set; } = 50;
}
