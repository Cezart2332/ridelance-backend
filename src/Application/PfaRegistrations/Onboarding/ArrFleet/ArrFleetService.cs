using Application.Abstractions.Data;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.ArrFleet;
using Microsoft.EntityFrameworkCore;

namespace Application.PfaRegistrations.Onboarding.ArrFleet;

/// <summary>Citirea și maparea pasului, comune clientului și adminului.</summary>
internal sealed class ArrFleetService(IApplicationDbContext context, ArrAgencyResolver agencyResolver)
{
    /// <summary>Dosarul cu tot ce citește pasul: conturile de platformă și cererea.</summary>
    public Task<PfaRegistration?> LoadAsync(
        Func<IQueryable<PfaRegistration>, IQueryable<PfaRegistration>> filter,
        CancellationToken cancellationToken) =>
        filter(context.PfaRegistrations
                .Include(r => r.PlatformAccounts)
                .Include(r => r.ArrFleetApplication)
                    .ThenInclude(a => a!.StatusLogs))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PfaRegistration?> LoadForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        LoadAsync(q => q.Where(r => r.UserId == userId).OrderByDescending(r => r.CreatedAtUtc), cancellationToken);

    public Task<PfaRegistration?> LoadByIdAsync(Guid registrationId, CancellationToken cancellationToken) =>
        LoadAsync(q => q.Where(r => r.Id == registrationId), cancellationToken);

    /// <summary>Cererea pasului; se creează la prima scriere, ca dosarele să nu aibă rânduri goale.</summary>
    public ArrFleetApplication Ensure(PfaRegistration registration, DateTime nowUtc)
    {
        if (registration.ArrFleetApplication is not null)
        {
            return registration.ArrFleetApplication;
        }

        var application = new ArrFleetApplication
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            UserId = registration.UserId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        context.ArrFleetApplications.Add(application);
        registration.ArrFleetApplication = application;
        return application;
    }

    /// <summary>Contul de șofer al platformei, creat dacă lipsește.</summary>
    public PfaPlatformAccount AccountFor(PfaRegistration registration, PfaPlatformProvider provider)
    {
        PfaPlatformAccount? account = registration.PlatformAccounts
            .Where(a => a.Provider == provider)
            .OrderBy(a => a.Kind == PfaPlatformAccountKind.Driver ? 0 : 1)
            .FirstOrDefault();

        if (account is not null)
        {
            return account;
        }

        account = new PfaPlatformAccount
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            Provider = provider,
            Kind = PfaPlatformAccountKind.Driver,
        };
        context.PfaPlatformAccounts.Add(account);
        registration.PlatformAccounts.Add(account);
        return account;
    }

    public Task<List<Document>> DocumentsAsync(Guid userId, CancellationToken cancellationToken) =>
        context.Documents
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .ToListAsync(cancellationToken);

    public async Task<ArrFleetStateResponse> ToResponseAsync(PfaRegistration registration, CancellationToken cancellationToken)
    {
        ArrFleetApplication application = registration.ArrFleetApplication ?? new ArrFleetApplication
        {
            PfaRegistrationId = registration.Id,
            UserId = registration.UserId,
        };

        List<Document> documents = await DocumentsAsync(registration.UserId, cancellationToken);

        var authorIds = application.StatusLogs
            .Where(l => l.ChangedByUserId is not null)
            .Select(l => l.ChangedByUserId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string> authors = authorIds.Count == 0
            ? []
            : await context.Users
                .AsNoTracking()
                .Where(u => authorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);

        var accounts = registration.PlatformAccounts.Where(a => a.IsSelectedByUser).ToList();

        var driverAccounts = ArrFleetRules.SelectedProviders(application.Platforms)
            .Select(provider =>
            {
                PfaPlatformAccount? account = accounts.FirstOrDefault(a => a.Provider == provider);
                return new ArrFleetDriverAccountDto(
                    provider.ToString(),
                    account?.DriverHasExistingAccount,
                    account?.DriverEmail,
                    account?.DriverPhone,
                    account?.DriverFullName,
                    account?.DriverHasExistingAccount == false);
            })
            .ToList();

        ArrAgencyResolution agency = await agencyResolver.ResolveAsync(registration, cancellationToken);

        var payments = ArrFleetRules.Payments(application.Platforms)
            .Select(p => new ArrFleetPaymentDto(
                p.Kind,
                p.Label,
                p.Explanation,
                p.AmountBani,
                p.ProofCategory.ToString(),
                ArrFleetRules.Latest(documents, p.ProofCategory) is not null))
            .ToList();

        return new ArrFleetStateResponse(
            registration.Id,
            application.Status.ToString(),
            ArrFleetRules.StatusLabel(application.Status),
            ArrFleetRules.SelectedProviders(application.Platforms).Select(p => p.ToString()).ToList(),
            driverAccounts,
            application.VehicleOwnership?.ToString(),
            application.PaymentAmountBani,
            payments,
            agency.Account is ArrAccount account
                ? new ArrAgencyDto(account.CountyCode, account.CountyName, account.BeneficiaryName, account.Treasury, account.FiscalCode, account.Iban)
                : null,
            agency.Error,
            ArrFleetRules.PaymentProofOutdated(application, documents),
            application.SubmittedAtUtc,
            application.SubmittedAtUtc is null ? application.ReopenedReason : null,
            ArrFleetRules.Missing(application, accounts, documents),
            application.StatusLogs
                .OrderByDescending(l => l.ChangedAtUtc)
                .Select(l => new ArrFleetStatusLogDto(
                    l.FromStatus.ToString(),
                    l.ToStatus.ToString(),
                    l.ChangedByUserId is Guid id && authors.TryGetValue(id, out string? name) ? name : null,
                    l.ChangedAtUtc))
                .ToList());
    }

    /// <summary>Schimbă statusul și îl scrie în jurnal: cine, când, din ce în ce.</summary>
    public void SetStatus(ArrFleetApplication application, ArrFleetStatus status, Guid? changedBy, DateTime nowUtc)
    {
        if (application.Status == status)
        {
            return;
        }

        var log = new ArrFleetStatusLog
        {
            Id = Guid.NewGuid(),
            ArrFleetApplicationId = application.Id,
            FromStatus = application.Status,
            ToStatus = status,
            ChangedByUserId = changedBy,
            ChangedAtUtc = nowUtc,
        };

        context.ArrFleetStatusLogs.Add(log);
        application.StatusLogs.Add(log);
        application.Status = status;
        application.UpdatedAtUtc = nowUtc;
    }
}
