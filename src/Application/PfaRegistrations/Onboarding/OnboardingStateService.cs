using Application.Abstractions.Data;
using Application.Payments;
using Domain.Documents;
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
    /// <param name="allowJustCompleted">
    /// Acceptă și un pas pe care userul tocmai l-a completat, cât timp onboardingul nu e închis.
    ///
    /// De ce: un pas al cărui status se derivă din datele userului se închide chiar în timpul
    /// completării. Pe Uber/Bolt Fleet, salvarea automată din timpul tastării parolei putea
    /// completa pasul, iar următoarea salvare — aceeași parolă, câteva litere mai încolo — era
    /// refuzată ca „pas inactiv". Formularul rămânea pe „Nesalvat" și nu se mai putea trece mai
    /// departe. Datele sunt ale userului și niciun admin nu le-a preluat încă, deci corectarea
    /// lor nu are ce strica.
    /// </param>
    public async Task<Result> EnsureWritableAsync(
        Guid userId,
        OnboardingStepKey step,
        CancellationToken cancellationToken,
        bool allowJustCompleted = false)
    {
        OnboardingStateResponse state = await GetForUserAsync(userId, cancellationToken);

        bool writable = OnboardingStepCatalog.IsWritableByUser(state.Steps, step)
            || allowJustCompleted
                && !state.AllSectionsValidated
                && OnboardingStepCatalog.IsCompleted(state.Steps, step);

        return writable
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
                // Emailul contului e sursa unică pentru precompletările din onboarding
                // (Oblio, Uber Fleet, Bolt Fleet) — vezi specul de fix-uri §5.
                .Include(r => r.User)
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

        // Documentele alimentează checklistul pasului curent (rail-ul dreapta). Le citim o dată,
        // aici, ca frontendul să nu mai numere singur ce lipsește.
        List<Document> documents = await context.Documents
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .ToListAsync(cancellationToken);

        bool hasPaidInfiintare = await InfiintarePaymentCheck.HasPaidAsync(context, userId, cancellationToken);
        bool hasFailedPayment = !hasPaidInfiintare
            && await InfiintarePaymentCheck.HasFailedAttemptAsync(context, userId, cancellationToken);

        if (registration is not null &&
            OnboardingProgress.TryMarkCompleted(registration, eligibility, DateTime.UtcNow))
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        // Pașii atinși de uneltele de dezvoltare. Se citesc doar pentru sesiunile marcate:
        // pentru restul e o interogare fără rost pe fiecare încărcare de pagină.
        List<string>? devSkippedSteps = registration?.IsDevSession == true
            ? await context.OnboardingStepAudits
                .AsNoTracking()
                .Where(a => a.PfaRegistrationId == registration.Id && a.FromStatus == "DevTools")
                .Select(a => a.StepKey)
                .Distinct()
                .ToListAsync(cancellationToken)
            : null;

        return OnboardingStateBuilder.Build(
            registration,
            hasPaidInfiintare,
            eligibility,
            hasFailedPayment,
            documents,
            devSkippedSteps,
            await CountyFromIdCardAsync(documents, cancellationToken));
    }

    /// <summary>
    /// Județul de domiciliu citit din buletin. E aceeași valoare pe care OCR-ul o scrie în dosarul
    /// de înființare, dar luată de la sursă: tabelul de câmpuri extrase.
    ///
    /// Trebuie citită de aici pentru că dosarul de înființare există doar pe ramura „Nu am PFA",
    /// iar buletinul se încarcă la eligibilitate — adică înainte ca dosarul să existe. Pe ramura
    /// „Am PFA" nu se creează niciodată un dosar, deci județul n-avea de unde să vină, iar
    /// selectul ARR rămânea gol deși informația era deja în sistem.
    /// </summary>
    private async Task<string?> CountyFromIdCardAsync(
        List<Document> documents,
        CancellationToken cancellationToken)
    {
        Guid[] identityDocumentIds = documents
            .Where(d => d.Status != DocumentStatus.Rejected
                && d.Category is DocumentCategory.CarteIdentitate or DocumentCategory.Buletin)
            .OrderByDescending(d => d.UploadedAtUtc)
            .Select(d => d.Id)
            .ToArray();

        if (identityDocumentIds.Length == 0)
        {
            return null;
        }

        List<ExtractedField> fields = await context.ExtractedFields
            .AsNoTracking()
            .Where(f => identityDocumentIds.Contains(f.DocumentId))
            .ToListAsync(cancellationToken);

        // Cel mai recent buletin întâi; în cadrul lui, valoarea confirmată de om bate OCR-ul.
        foreach (Guid documentId in identityDocumentIds)
        {
            ExtractedField? county = fields.Find(f => f.DocumentId == documentId
                && string.Equals(f.FieldKey, "domiciliu_judet", StringComparison.OrdinalIgnoreCase));

            string? value = county?.ConfirmedValue ?? county?.AiNormalizedValue;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
