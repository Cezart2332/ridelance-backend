namespace Infrastructure.FiscalLink;

/// <summary>
/// Contul de integrator FiscalLink. Secțiunea <c>FiscalLink</c>; în producție, cheile ca variabile
/// de mediu (<c>FiscalLink__ManagementKey</c>).
/// </summary>
/// <remarks>
/// Cheile nu sunt interschimbabile: cea de gestiune creează clienți și citește casele, dar nu poate
/// tipări; cea de comenzi tipărește, dar nu poate modifica clienți. Cheile reale încep cu
/// <c>fsk_</c>, cele sandbox cu <c>fsk_test_</c>.
/// </remarks>
public sealed class FiscalLinkOptions
{
    public const string SectionName = "FiscalLink";

    /// <summary>Adresa de bază a integratorului, cu tenantul RIDElance.</summary>
    public Uri BaseUrl { get; set; } = new("https://cloud-api.fiscallink.ro/api/tenants/b6ff97d6-318f-417a-a0da-8e29325e174a/integrator/");

    /// <summary>Cheia de gestiune (Setări → Chei API → Gestiune).</summary>
    public string? ManagementKey { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ManagementKey);
}
