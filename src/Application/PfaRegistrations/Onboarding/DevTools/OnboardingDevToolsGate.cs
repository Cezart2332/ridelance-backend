using Microsoft.Extensions.Configuration;

namespace Application.PfaRegistrations.Onboarding.DevTools;

/// <summary>
/// Poarta uneltelor de dezvoltare pentru onboarding.
///
/// State machine-ul e server-side, deci saltul între pași trebuie autorizat AICI — ascunderea
/// butonului din UI n-ar însemna nimic, endpoint-urile ar rămâne apelabile.
///
/// Uneltele sunt <b>pornite implicit</b>: nu cer nicio variabilă de mediu, niciun flag de build
/// și nicio listă de utilizatori. Singurul comutator e <c>Onboarding:DevTools:Enabled</c>, iar
/// el trebuie setat explicit pe <c>false</c> ca să le stingă.
///
/// Consecința, asumată la cererea proprietarului produsului: pe orice mediu unde nu e stins,
/// <b>orice utilizator autentificat</b> poate sări peste pași. Un dosar atins de unelte devine
/// sesiune de test (fără plăți reale, dosare cu filigran „TEST"), deci nu poate fi confundat cu
/// unul real — dar nici nu mai poate fi dus la capăt ca dosar valid.
///
/// Când poarta nu trece, endpoint-urile răspund <b>404</b>, nu 403: un 403 ar confirma că ruta
/// există.
/// </summary>
public sealed class OnboardingDevToolsGate(IConfiguration configuration)
{
    private const string EnabledKey = "Onboarding:DevTools:Enabled";

    /// <summary>
    /// Uneltele sunt disponibile. Implicit da; se sting punând <c>Onboarding:DevTools:Enabled</c>
    /// pe <c>false</c> (sau <c>Onboarding__DevTools__Enabled=false</c> în mediu).
    /// </summary>
    public bool IsAvailable =>
        !bool.TryParse(configuration[EnabledKey], out bool configured) || configured;

    /// <summary>
    /// Pentru un utilizator anume. Nu mai există allowlist — rămâne doar comutatorul global.
    /// <paramref name="userId"/> stă în semnătură fiindcă auditul îl scrie oricum, iar
    /// reintroducerea unei restricții pe utilizator nu trebuie să schimbe apelanții.
    /// </summary>
    public Task<bool> IsAllowedAsync(Guid userId, CancellationToken cancellationToken)
    {
        _ = userId;
        _ = cancellationToken;

        return Task.FromResult(IsAvailable);
    }
}
