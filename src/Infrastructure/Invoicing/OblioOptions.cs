namespace Infrastructure.Invoicing;

public sealed class OblioOptions
{
    public const string SectionName = "Oblio";

    /// <summary>Email-ul contului Oblio (client_id pentru OAuth).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Token-ul API din Oblio → Setări → Date cont (client_secret).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>CIF-ul firmei din Oblio pe care se emit facturile.</summary>
    public string Cif { get; set; } = string.Empty;

    /// <summary>Numele seriei de facturi din Oblio (ex. "RDL").</summary>
    public string SeriesName { get; set; } = string.Empty;

    /// <summary>
    /// Trimite facturile emise și către SPV (e-Factura). Implicit dezactivat —
    /// se activează cu Oblio__SendToSpv=true doar când firma e pregătită.
    /// </summary>
    public bool SendToSpv { get; set; }

    public string BaseUrl { get; set; } = "https://www.oblio.eu/api";
}
