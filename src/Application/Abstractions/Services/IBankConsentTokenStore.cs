namespace Application.Abstractions.Services;

/// <summary>
/// De unde își ia furnizorul tokenurile unui consimțământ și unde le pune înapoi după reînnoire.
///
/// Există ca să nu dăm furnizorului o bază de date. El știe să vorbească HTTP și să reînnoiască un
/// token expirat; unde stau tokenurile și cum sunt criptate e treaba infrastructurii, iar interfața
/// asta e exact granița dintre cele două.
///
/// Contract: <see cref="SaveAsync"/> se cheamă imediat după fiecare reînnoire, nu la finalul
/// operațiunii. Tokenul de reîmprospătare se rotește la fiecare folosire — dacă apelul următor
/// eșuează și nu am salvat, consimțământul rămâne cu o pereche pe care furnizorul a invalidat-o
/// deja, iar utilizatorul trebuie să reautorizeze la bancă degeaba.
/// </summary>
public interface IBankConsentTokenStore
{
    Task<BankConsentTokens?> GetAsync(string consentId, CancellationToken cancellationToken = default);

    Task SaveAsync(string consentId, BankConsentTokens tokens, CancellationToken cancellationToken = default);

    /// <summary>
    /// IP-ul clientului de pe consimțământ, pentru antetul PSU-IP-Address.
    ///
    /// Furnizorul îl cere la fiecare apel, nu doar la deschidere, iar jobul de sincronizare n-are
    /// cerere HTTP din care să-l ia — deci vine tot de pe rândul conexiunii, ca tokenurile.
    /// </summary>
    Task<string?> GetPsuIpAddressAsync(string consentId, CancellationToken cancellationToken = default);
}
