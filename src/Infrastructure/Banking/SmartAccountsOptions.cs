namespace Infrastructure.Banking;

/// <summary>
/// Smart Accounts (Smart Fintech) — furnizorul de open banking, autorizat BNR.
///
/// Spre deosebire de furnizorul anterior, aici nu există un token de cont: fiecare consimțământ
/// își are propria pereche de tokenuri, iar ce se configurează global e doar identitatea noastră
/// de partener — <see cref="ClientId"/> plus certificatul cu care se ridică canalul mTLS.
/// </summary>
public sealed class SmartAccountsOptions
{
    public const string SectionName = "SmartAccounts";

    /// <summary>
    /// Identificatorul de partener, asociat de ei cu Subject DN-ul certificatului nostru. Nu e un
    /// secret în sine — fără certificatul potrivit nu deschide nimic.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Gazda pe care se fac apelurile obișnuite (bănci, consimțământ, conturi).</summary>
    public string ApiBaseUrl { get; set; } = "https://api.sandboxaccounts.smartfintech.eu";

    /// <summary>
    /// Gazda pe care se emit tokenurile. E separată fiindcă doar acolo se cere certificatul de
    /// client; restul apelurilor merg cu tokenul primit aici.
    /// </summary>
    public string MtlsBaseUrl { get; set; } = "https://mtls.sandboxaccounts.smartfintech.eu";

    /// <summary>
    /// Certificatul și cheia, în PEM, direct din configurare. Drumul pentru producție: ajung prin
    /// variabile de mediu (<c>SmartAccounts__CertificatePem</c>), ca restul secretelor, deci cheia
    /// privată nu intră nici în imagine, nici în repo.
    /// </summary>
    public string CertificatePem { get; set; } = string.Empty;

    public string PrivateKeyPem { get; set; } = string.Empty;

    /// <summary>
    /// Aceleași două, ca fișiere pe disc. Drumul pentru dezvoltare: certificatul de sandbox vine
    /// de la ei ca pereche de fișiere, iar folderul e ignorat de git. Se citesc doar dacă
    /// <see cref="CertificatePem"/> e gol.
    /// </summary>
    public string CertificatePath { get; set; } = string.Empty;

    public string PrivateKeyPath { get; set; } = string.Empty;

    /// <summary>
    /// Cât timp e valabil un consimțământ, în zile. API-ul acceptă 90–180 și respinge orice
    /// altceva cu „Bad request"; 180 e maximul, adică cea mai rară reautorizare la bancă.
    /// </summary>
    public int ConsentValidDays { get; set; } = 180;

    /// <summary>Câte zile de istoric se cer la prima sincronizare a unui cont.</summary>
    public int InitialHistoryDays { get; set; } = 365;

    /// <summary>Câte pagini de tranzacții acceptăm într-un singur apel, ca plasă contra buclelor.</summary>
    public int MaxTransactionPages { get; set; } = 50;
}
