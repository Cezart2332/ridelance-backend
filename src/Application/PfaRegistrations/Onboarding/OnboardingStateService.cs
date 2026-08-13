using Application.Abstractions.Data;
using Application.Payments;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding;

/// <summary>
/// Punctul unic prin care se citește și se validează starea onboardingului. Înainte, încărcarea
/// grafului era duplicată în handlerul clientului și în cel de admin — cu o diferență: doar primul
/// stampila înrolarea, deci cele două citiri puteau raporta lucruri diferite despre același dosar.
/// Regulile de tranziție nu existau nicăieri, așa că un client putea scrie pe pasul 5 cu pasul 2
/// neînceput. Ambele probleme se rezolvă aici, o singură dată.
/// </summary>
public sealed class OnboardingStateService(IApplicationDbContext context)
{
    /// <summary>
    /// Starea văzută de șofer. Stampilează înrolarea dacă tocmai s-a completat. Întoarce o stare
    /// validă și fără dosar: pasul de eligibilitate se parcurge înainte să existe un
    /// <see cref="PfaRegistration"/>.
    /// </summary>
    public Task<OnboardingStateResponse> GetForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        BuildAsync(q => q.Where(r => r.UserId == userId).OrderByDescending(r => r.CreatedAtUtc), userId, cancellationToken);

    /// <summary>
    /// Aceeași stare, adresată prin dosar (admin). Trece prin exact același cod ca varianta
    /// clientului — inclusiv stampila de înrolare — ca cele două să nu poată diverge.
    /// </summary>
    public async Task<Result<OnboardingStateResponse>> GetForRegistrationAsync(
        Guid registrationId,
        CancellationToken cancellationToken)
    {
        Guid? userId = await context.PfaRegistrations
            .AsNoTracking()
            .Where(r => r.Id == registrationId)
            .Select(r => (Guid?)r.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId is null)
        {
            return Result.Failure<OnboardingStateResponse>(PfaRegistrationErrors.NotFound(registrationId));
        }

        OnboardingStateResponse state = await BuildAsync(
            q => q.Where(r => r.Id == registrationId), userId.Value, cancellationToken);

        return Result.Success(state);
    }

    /// <summary>
    /// Poarta de scriere. Se apelează prima în handlerele de pas, înainte de orice validare de
    /// conținut: dacă pasul nu e activ, un mesaj despre câmpuri lipsă ar fi oricum irelevant.
    /// </summary>
    public async Task<Result> EnsureWritableAsync(
        Guid userId,
        OnboardingStepKey step,
        CancellationToken cancellationToken)
    {
        OnboardingStateResponse state = await GetForUserAsync(userId, cancellationToken);

        return OnboardingStepCatalog.IsWritableByUser(state.Steps, step)
            ? Result.Success()
            : Result.Failure(OnboardingErrors.StepNotActive(OnboardingStepCatalog.LabelOf(step)));
    }

    private async Task<OnboardingStateResponse> BuildAsync(
        Func<IQueryable<PfaRegistration>, IQueryable<PfaRegistration>> filter,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Tracked: aceeași citire e cea care stampilează înrolarea când se închide ultimul pas.
        PfaRegistration? registration = await filter(context.PfaRegistrations
                .Include(r => r.OnboardingSections)
                .Include(r => r.FiscalProfile)
                .Include(r => r.BankAccountDeclaration)
                .Include(r => r.OblioAccount)
                // Pasul fiscal se închide pe pachetul de semnături alocat de admin (RL-02).
                .Include(r => r.SignaturePacket)
                .Include(r => r.ArrAuthorizationRequest)
                .Include(r => r.PlatformAccounts)
                .Include(r => r.Vehicles).ThenInclude(v => v.CopyRequest)
                // Ramura „Nu am PFA”: pasul PFA se închide pe dosarul semnat, nu pe plată.
                .Include(r => r.CompanyFormationRequest))
            .FirstOrDefaultAsync(cancellationToken);

        OnboardingEligibilityProfile? eligibility = await context.OnboardingEligibilityProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == userId, cancellationToken);

        bool hasPaidInfiintare = await InfiintarePaymentCheck.HasPaidAsync(context, userId, cancellationToken);

        if (registration is not null &&
            OnboardingProgress.TryMarkCompleted(registration, eligibility, DateTime.UtcNow))
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return OnboardingStateBuilder.Build(registration, hasPaidInfiintare, eligibility);
    }
}
