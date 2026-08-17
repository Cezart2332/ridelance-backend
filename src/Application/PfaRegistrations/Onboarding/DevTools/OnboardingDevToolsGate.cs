using Application.Abstractions.Data;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Application.PfaRegistrations.Onboarding.DevTools;

/// <summary>
/// Poarta uneltelor de dezvoltare pentru onboarding (spec fix-uri §13.1).
///
/// State machine-ul e server-side, deci saltul între pași trebuie autorizat AICI. Ascunderea
/// butonului din UI n-ar însemna nimic: endpoint-urile ar rămâne apelabile.
///
/// Trei condiții, toate necesare:
///
/// 1. <c>Onboarding:DevTools:Enabled</c> pe true în configurație;
/// 2. mediul NU e Production — indiferent ce zice configurația;
/// 3. utilizatorul e în allowlist: lista explicită de id-uri din
///    <c>Onboarding:DevTools:UserIds</c>, sau un cont de Admin.
///
/// Specul cerea un rol dedicat (<c>OnboardingDevTester</c>). Modelul de roluri de aici e un
/// singur enum pe utilizator, nu o colecție: un rol nou i-ar lua testerului rolul de
/// <c>Client</c>, adică exact fluxul pe care vrea să-l testeze. De aceea allowlist-ul e pe
/// id-uri — la fel de explicit, fără efect asupra parcursului testat.
///
/// Când poarta nu trece, endpoint-urile răspund <b>404</b>, nu 403: un 403 ar confirma că ruta
/// există.
/// </summary>
public sealed class OnboardingDevToolsGate(
    IApplicationDbContext context,
    IConfiguration configuration)
{
    private const string EnabledKey = "Onboarding:DevTools:Enabled";
    private const string UserIdsKey = "Onboarding:DevTools:UserIds";
    private const string EnvironmentKey = "ASPNETCORE_ENVIRONMENT";

    /// <summary>
    /// Mediul, citit din configurație pentru că stratul Application nu cunoaște hostingul.
    /// Necunoscut înseamnă Production: poarta se închide, nu se deschide, când nu știm unde
    /// rulăm.
    /// </summary>
    private bool IsProduction =>
        !string.Equals(configuration[EnvironmentKey], "Development", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(configuration[EnvironmentKey], "Staging", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Nivelurile 1 și 2, fără atingerea bazei. Se poate evalua și fără un utilizator — de
    /// exemplu ca să știm dacă are rost să interogăm allowlist-ul.
    /// </summary>
    public bool IsAvailable =>
        !IsProduction
        && bool.TryParse(configuration[EnabledKey], out bool enabled)
        && enabled;

    /// <summary>Toate cele trei niveluri, pentru un utilizator anume.</summary>
    public async Task<bool> IsAllowedAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return false;
        }

        bool allowlisted = configuration.GetSection(UserIdsKey)
            .GetChildren()
            .Any(entry => string.Equals(entry.Value, userId.ToString(), StringComparison.OrdinalIgnoreCase));

        if (allowlisted)
        {
            return true;
        }

        return await context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.Role == UserRole.Admin, cancellationToken);
    }
}
