namespace Infrastructure.Banking;

public sealed class EnableBankingOptions
{
    public const string SectionName = "EnableBanking";

    /// <summary>Application ID din Enable Banking Control Panel (enablebanking.com/cp/applications).</summary>
    public string ApplicationId { get; set; } = string.Empty;

    /// <summary>
    /// Cheia privată a aplicației (fișierul .pem descărcat la înregistrare).
    /// Acceptă PEM-ul brut (cu newline-uri sau cu "\n" escapate) sau PEM-ul codat base64 —
    /// recomandat pentru variabile de mediu: <c>base64 -w0 app.pem</c>.
    /// </summary>
    public string PrivateKeyPem { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.enablebanking.com";

    /// <summary>Tipul utilizatorului trimis la autorizare: "personal" sau "business".</summary>
    public string PsuType { get; set; } = "personal";

    /// <summary>
    /// Când e true, banca de test "Mock ASPSP" apare în lista de bănci.
    /// Aplicațiile sandbox Enable Banking pot autoriza doar bănci de test.
    /// </summary>
    public bool UseSandboxInstitution { get; set; }
}
