using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Services;
using Domain.FiscalProfiles;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Application.FiscalProfiles;

/// <summary>
/// Tot ce au în comun endpointurile profilului fiscal: cine are voie la ce profil, crearea
/// profilului unui an, datele precompletate, întrebările condiționate și salvarea cu verificarea
/// reviziei. Handlerii rămân doar pașii specifici fiecărei acțiuni.
/// </summary>
internal sealed class FiscalProfileService(
    IApplicationDbContext context,
    IUserContext userContext,
    ICompanyLookupService companyLookup,
    IDateTimeProvider clock,
    ILogger<FiscalProfileService> logger)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static readonly Error PfaNotFound =
        Error.NotFound("FiscalProfile.PfaNotFound", "Nu am găsit PFA-ul.");

    public static readonly Error Conflict = Error.Conflict(
        "FiscalProfile.Conflict",
        "Profilul a fost modificat de altcineva. Reîncarcă pentru a vedea ultima versiune.");

    public static readonly Error InvalidYear =
        Error.Problem("FiscalProfile.InvalidYear", "Anul fiscal nu e valid.");

    private static readonly TimeZoneInfo Romania = TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");

    public Guid CallerId => userContext.UserId;

    public static DateTime ToRomania(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Romania);

    /// <summary>Anul fiscal curent, după ceasul din România.</summary>
    public int CurrentTaxYear => ToRomania(clock.UtcNow).Year;

    public static bool IsValidYear(int year) => year is >= 2020 and <= 2100;

    /// <summary>
    /// Dosarul la care are acces cel care cheamă. Autorizarea stă aici, nu în UI: un PFA își
    /// vede doar propriul profil, contabilul doar clienții alocați lui, adminul pe toți.
    /// Un dosar interzis răspunde ca unul inexistent — nu confirmăm că există.
    /// </summary>
    public async Task<Result<PfaRegistration>> ResolveAsync(
        FiscalProfileScope scope,
        Guid? pfaRegistrationId,
        CancellationToken cancellationToken)
    {
        if (scope == FiscalProfileScope.Pfa)
        {
            PfaRegistration? own = await context.PfaRegistrations
                .Include(p => p.User)
                .Where(p => p.UserId == userContext.UserId)
                .OrderByDescending(p => p.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            return own is null ? Result.Failure<PfaRegistration>(PfaNotFound) : own;
        }

        UserRole? role = await context.Users
            .AsNoTracking()
            .Where(u => u.Id == userContext.UserId)
            .Select(u => (UserRole?)u.Role)
            .SingleOrDefaultAsync(cancellationToken);

        PfaRegistration? pfa = await context.PfaRegistrations
            .Include(p => p.User)
            .SingleOrDefaultAsync(p => p.Id == pfaRegistrationId, cancellationToken);

        bool allowed = pfa is not null && scope switch
        {
            FiscalProfileScope.Admin => role == UserRole.Admin,
            FiscalProfileScope.Accounting => role == UserRole.Contabil && pfa.AssignedContabilId == userContext.UserId,
            _ => false,
        };

        return allowed ? pfa! : Result.Failure<PfaRegistration>(PfaNotFound);
    }

    /// <summary>
    /// Profilul anului. Se creează la prima citire, cu datele precompletate și, pentru un an nou,
    /// cu răspunsurile de anul trecut — dar mereu în <c>NOT_STARTED</c>: estimările noului an
    /// rămân ascunse până le confirmă PFA-ul.
    /// </summary>
    public async Task<PfaTaxProfile> GetOrCreateAsync(PfaRegistration pfa, int year, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pfa);

        PfaTaxProfile? existing = await context.PfaTaxProfiles
            .SingleOrDefaultAsync(p => p.PfaRegistrationId == pfa.Id && p.TaxYear == year, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        PfaTaxProfile? previous = await context.PfaTaxProfiles
            .AsNoTracking()
            .Where(p => p.PfaRegistrationId == pfa.Id && p.TaxYear < year)
            .OrderByDescending(p => p.TaxYear)
            .FirstOrDefaultAsync(cancellationToken);

        DateTime now = clock.UtcNow;
        var profile = new PfaTaxProfile
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfa.Id,
            TaxYear = year,
            Status = PfaTaxProfileStatus.NotStarted,
            AccessGrantedAtUtc = pfa.OnboardingCompletedAtUtc,
            // Modalul automat e o singură dată per PFA, nu per an.
            FirstPromptShownAtUtc = previous?.FirstPromptShownAtUtc,
            PfaRegisteredOn = previous?.PfaRegisteredOn,
            PfaRegisteredOnSource = previous?.PfaRegisteredOnSource,
            PfaRegisteredOnObservedAtUtc = previous?.PfaRegisteredOnObservedAtUtc,
            // Situația (pensionar, student, angajat) se poate schimba de la un an la altul, dar de
            // obicei rămâne: anul nou pornește de la ea, iar PFA-ul doar o confirmă.
            AnswersJson = previous is null ? "{}" : Serialize(Deserialize(previous.AnswersJson)),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        if (profile.PfaRegisteredOn is null)
        {
            await PrefillRegistrationDateAsync(pfa, profile, cancellationToken);
        }

        context.PfaTaxProfiles.Add(profile);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return profile;
        }
        catch (DbUpdateException)
        {
            // Două cereri simultane au creat același profil; indexul unic a păstrat una.
            context.PfaTaxProfiles.Entry(profile).State = EntityState.Detached;
            return await context.PfaTaxProfiles
                .SingleAsync(p => p.PfaRegistrationId == pfa.Id && p.TaxYear == year, cancellationToken);
        }
    }

    /// <summary>
    /// Răspunsurile salvate. Cheile vechiului formular (alte venituri, pierderi, opțiuni) se
    /// ignoră; un profil vechi cu <c>employment: "full"</c> rămâne „angajat full-time”.
    /// </summary>
    public static FiscalProfileAnswers Deserialize(string json)
    {
        string text = string.IsNullOrWhiteSpace(json) ? "{}" : json;
        FiscalProfileAnswers answers = JsonSerializer.Deserialize<FiscalProfileAnswers>(text, Json) ?? new FiscalProfileAnswers();
        if (answers.EmployedFullTime is not null)
        {
            return answers;
        }

        using var document = JsonDocument.Parse(text);
        bool legacy = document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("employment", out JsonElement employment)
            && employment.ValueKind == JsonValueKind.String;
        if (!legacy)
        {
            return answers;
        }

        bool fullTime = document.RootElement.GetProperty("employment").GetString() == "full";
        return answers with { EmployedFullTime = fullTime ? FiscalProfileSchema.Yes : FiscalProfileSchema.No };
    }

    public static string Serialize(FiscalProfileAnswers answers) => JsonSerializer.Serialize(answers, Json);

    public async Task<FiscalProfileResponse> ToResponseAsync(
        PfaRegistration pfa,
        PfaTaxProfile profile,
        bool includeCorrections,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pfa);
        ArgumentNullException.ThrowIfNull(profile);

        FiscalProfileActor? lastChangedBy = profile.LastChangedByUserId is Guid actorId
            ? await ActorAsync(actorId, pfa, cancellationToken)
            : null;

        List<DataCorrectionResponse> corrections = includeCorrections
            ? await context.PfaDataCorrectionRequests
                .AsNoTracking()
                .Where(c => c.PfaRegistrationId == pfa.Id)
                .OrderByDescending(c => c.CreatedAtUtc)
                .Take(20)
                .Select(c => new DataCorrectionResponse(c.Id, c.Fields, c.Details, c.State.ToString(), c.CreatedAtUtc, c.ResolvedAtUtc))
                .ToListAsync(cancellationToken)
            : [];

        var facts = new FiscalProfileFacts(
            pfa.LegalName ?? pfa.HolderName ?? pfa.FullName,
            pfa.Cui,
            new FiscalProfileFact<DateOnly?>(profile.PfaRegisteredOn, profile.PfaRegisteredOnSource ?? "necunoscut", profile.PfaRegisteredOnObservedAtUtc),
            // Onboardingul nu întreabă încă data începerii activității. Nu se copiază din data
            // înființării: dacă lipsește, PFA-ul o dă printr-o cerere de corectare.
            new FiscalProfileFact<DateOnly?>(null, "onboarding", null),
            new FiscalProfileFact<DateTime?>(profile.AccessGrantedAtUtc, "RIDElance", profile.AccessGrantedAtUtc),
            profile.Regime);

        return new FiscalProfileResponse(
            profile.Id,
            pfa.Id,
            profile.TaxYear,
            profile.Regime,
            StatusCode(profile.Status),
            Deserialize(profile.AnswersJson),
            profile.Revision,
            profile.FirstPromptShownAtUtc,
            profile.CompletedAtUtc,
            profile.EstimatedTaxesUnlockedAtUtc,
            profile.UpdatedAtUtc,
            lastChangedBy,
            facts,
            corrections);
    }

    public static string StatusCode(PfaTaxProfileStatus status) => status switch
    {
        PfaTaxProfileStatus.Draft => "DRAFT",
        PfaTaxProfileStatus.Completed => "COMPLETED",
        _ => "NOT_STARTED",
    };

    public static string RoleCode(FiscalProfileScope scope) => scope switch
    {
        FiscalProfileScope.Admin => "admin",
        FiscalProfileScope.Accounting => "accounting",
        _ => "pfa",
    };

    public async Task<FiscalProfileActor> ActorAsync(Guid userId, PfaRegistration pfa, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pfa);

        var user = await context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FirstName, u.LastName, u.Email, u.Role })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return new FiscalProfileActor(userId, "Utilizator șters", "unknown");
        }

        string role = userId == pfa.UserId
            ? "pfa"
            : user.Role switch
            {
                UserRole.Admin => "admin",
                UserRole.Contabil => "accounting",
                _ => "unknown",
            };

        string name = $"{user.FirstName} {user.LastName}".Trim();
        return new FiscalProfileActor(userId, name.Length > 0 ? name : user.Email, role);
    }

    /// <summary>
    /// Verificarea optimistă a reviziei (If-Match). <c>null</c> înseamnă că clientul n-a trimis
    /// nimic — acceptat doar unde handlerul o permite explicit.
    /// </summary>
    public static Result CheckRevision(PfaTaxProfile profile, int? expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return expectedRevision is int expected && expected != profile.Revision
            ? Result.Failure(Conflict)
            : Result.Success();
    }

    public static ValidationError ToValidationError(Dictionary<string, string> errors) =>
        new(errors.Select(e => Error.Problem(e.Key, e.Value)).ToArray());

    public void AddRevision(
        PfaTaxProfile profile,
        FiscalProfileScope scope,
        IReadOnlyList<FiscalProfileFieldChange> changes,
        string? reason)
    {
        ArgumentNullException.ThrowIfNull(profile);

        context.PfaTaxProfileRevisions.Add(new PfaTaxProfileRevision
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            Revision = profile.Revision,
            ActorUserId = userContext.UserId,
            ActorRole = RoleCode(scope),
            ChangesJson = JsonSerializer.Serialize(changes, Json),
            Reason = reason,
            CreatedAtUtc = clock.UtcNow,
        });
    }

    /// <summary>Marchează o schimbare: revizie nouă, cine, când.</summary>
    public void Touch(PfaTaxProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Revision++;
        profile.UpdatedAtUtc = clock.UtcNow;
        profile.LastChangedByUserId = userContext.UserId;
    }

    public DateTime UtcNow => clock.UtcNow;

    /// <summary>
    /// Salvarea, cu revizia ca token de concurență: dacă altcineva a scris între citire și
    /// salvare, răspunsul e 409, nu o suprascriere oarbă.
    /// </summary>
    public async Task<Result> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(Conflict);
        }
    }

    private async Task PrefillRegistrationDateAsync(PfaRegistration pfa, PfaTaxProfile profile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pfa.Cui))
        {
            return;
        }

        try
        {
            CompanyLookupResult? company = await companyLookup.FindByCuiAsync(pfa.Cui, cancellationToken);
            if (company?.RegistrationDate is string raw && TryParseDate(raw, out DateOnly registered))
            {
                profile.PfaRegisteredOn = registered;
                profile.PfaRegisteredOnSource = "ANAF";
                profile.PfaRegisteredOnObservedAtUtc = clock.UtcNow;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // Fără ANAF, data rămâne necunoscută; profilul se poate completa oricum.
            logger.LogWarning(ex, "Nu am putut citi data înființării PFA {PfaId} de la ANAF.", pfa.Id);
        }
    }

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd.MM.yyyy", "yyyy/MM/dd", "dd/MM/yyyy"];

    internal static bool TryParseDate(string raw, out DateOnly date) =>
        DateOnly.TryParseExact(raw.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
