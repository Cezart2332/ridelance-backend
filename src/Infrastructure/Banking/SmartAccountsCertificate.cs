using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace Infrastructure.Banking;

/// <summary>
/// Certificatul de client cu care se ridică mTLS către Smart Accounts.
///
/// Se încarcă o singură dată și leneș. Leneș, fiindcă lipsa lui nu trebuie să doboare pornirea
/// aplicației: pe un mediu fără open banking configurat, restul platformei merge mai departe și
/// doar <c>IsConfigured</c> devine fals. O excepție în constructor ar transforma o integrare
/// neconfigurată într-un server care nu pornește.
/// </summary>
internal class SmartAccountsCertificate(IOptions<SmartAccountsOptions> options)
{
    private readonly SmartAccountsOptions _options = options.Value;
    private readonly Lock _gate = new();

    private X509Certificate2? _certificate;
    private bool _loaded;

    /// <summary>
    /// Există un certificat utilizabil? Virtual ca testele să poată exercita providerul fără să
    /// aibă o cheie privată pe disc — încărcarea unui certificat real nu e ce verifică ele.
    /// </summary>
    public virtual bool IsPresent => Value is not null;

    /// <summary>Certificatul, sau null dacă nu e configurat sau nu s-a putut citi.</summary>
    public X509Certificate2? Value
    {
        get
        {
            lock (_gate)
            {
                if (!_loaded)
                {
                    _certificate = Load();
                    _loaded = true;
                }

                return _certificate;
            }
        }
    }

    private X509Certificate2? Load()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_options.CertificatePem) &&
                !string.IsNullOrWhiteSpace(_options.PrivateKeyPem))
            {
                return Persistable(X509Certificate2.CreateFromPem(
                    NormalizePem(_options.CertificatePem),
                    NormalizePem(_options.PrivateKeyPem)));
            }

            if (!string.IsNullOrWhiteSpace(_options.CertificatePath) &&
                !string.IsNullOrWhiteSpace(_options.PrivateKeyPath) &&
                File.Exists(_options.CertificatePath) &&
                File.Exists(_options.PrivateKeyPath))
            {
                return Persistable(X509Certificate2.CreateFromPemFile(_options.CertificatePath, _options.PrivateKeyPath));
            }
        }
        catch (Exception)
        {
            // Un certificat stricat se tratează ca unul lipsă: integrarea se raportează
            // neconfigurată, iar apelurile eșuează cu un mesaj al nostru, nu cu o excepție de
            // criptografie scăpată prin toate straturile.
            return null;
        }

        return null;
    }

    /// <summary>
    /// Aduce la formă un PEM venit prin variabilă de mediu.
    ///
    /// Un PEM are linii, iar interfețele de configurare le tratează fiecare altfel: unele le
    /// păstrează, altele le transformă în „\n" literal, iar cine vrea să scape de problemă
    /// codează tot blocul în base64. Le acceptăm pe toate trei — altfel un certificat corect,
    /// lipit într-un câmp care nu ține newline-uri, ar da doar „integrare neconfigurată", fără
    /// niciun indiciu că de acolo vine.
    /// </summary>
    private static string NormalizePem(string value)
    {
        string text = value.Trim();

        if (!text.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            try
            {
                text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(text));
            }
            catch (FormatException)
            {
                // Nu e base64. Îl lăsăm cum e; ce urmează îl va respinge, cu același efect.
            }
        }

        return text.Replace("\\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// Pe Windows, un certificat construit din PEM are cheia efemeră, iar SslStream refuză să o
    /// folosească („no credentials are available"). Reimportul prin PFX o leagă corect. Pe Linux
    /// nu e nevoie, dar nici nu strică — o singură cale de cod pentru ambele.
    /// </summary>
    private static X509Certificate2 Persistable(X509Certificate2 certificate)
    {
        using (certificate)
        {
            return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), password: null);
        }
    }
}
