using System.Globalization;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Notifications.RecurringDocumentation;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.GetContabilStats;

public sealed record ContabilStatsResponse(
    int TotalClients,
    int DocsToVerify,
    int MissingMonthlyDocs,
    int ReadyToProcess,
    int ProcessedThisMonth,
    int UnreadMessages,
    string MonthLabel);

public sealed record GetContabilStatsQuery(int? Year = null, int? Month = null) : IQuery<ContabilStatsResponse>;

internal sealed class GetContabilStatsQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetContabilStatsQuery, ContabilStatsResponse>
{
    public async Task<Result<ContabilStatsResponse>> Handle(
        GetContabilStatsQuery query,
        CancellationToken cancellationToken)
    {
        Guid accountantId = userContext.UserId;

        // Fetch caller to verify role
        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == accountantId, cancellationToken);

        if (caller is null || caller.Role != UserRole.Contabil)
        {
            return Result.Failure<ContabilStatsResponse>(
                Error.Failure("Auth.Unauthorized", "Doar contabilii pot accesa aceste statistici."));
        }

        // Reference instant: mid-month of the requested period, or now for the current month.
        DateTime referenceUtc = query.Year is int y && query.Month is int m && m >= 1 && m <= 12
            ? new DateTime(y, m, 15, 12, 0, 0, DateTimeKind.Utc)
            : DateTime.UtcNow;

        (DateTime monthStartUtc, DateTime monthEndUtc) = RecurringDocumentationTexts.GetRomaniaMonthBoundsUtc(referenceUtc);
        (int currentYear, int currentMonth) = RecurringDocumentationTexts.GetRomaniaYearMonth(referenceUtc);

        // Month label in Romanian (e.g. "Iunie 2026")
        string monthLabel = referenceUtc.ToString("MMMM yyyy", new CultureInfo("ro-RO"));
        // Capitalize first letter of month label
        if (!string.IsNullOrEmpty(monthLabel))
        {
            monthLabel = char.ToUpper(monthLabel[0], CultureInfo.InvariantCulture) + monthLabel[1..];
        }

        // 1. Total Clienți PFA
        int totalClients = await context.PfaRegistrations
            .CountAsync(p => p.AssignedContabilId == accountantId, cancellationToken);

        // 2. Documente de verificat (unverified monthly documents)
        DocumentCategory[] categories = new[]
        {
            DocumentCategory.ExtrasBancar,
            DocumentCategory.RaportUber,
            DocumentCategory.RaportBolt,
            DocumentCategory.Cheltuiala
        };

        int docsToVerify = await context.Documents
            .Where(d => context.PfaRegistrations.Any(p => p.UserId == d.UserId && p.AssignedContabilId == accountantId)
                && categories.Contains(d.Category)
                && d.Status == DocumentStatus.Pending)
            .CountAsync(cancellationToken);

        // 3. Get all assigned PFAs
        var pfas = (await context.PfaRegistrations
            .Where(p => p.AssignedContabilId == accountantId)
            .Select(p => new { p.Id, p.UserId })
            .ToListAsync(cancellationToken))
            .Select(p => (p.Id, p.UserId))
            .ToList();

        var clientUserIds = pfas.Select(p => p.UserId).ToList();
        var pfaIds = pfas.Select(p => p.Id).ToList();

        // Get all documents uploaded in the current month for these clients
        var monthDocs = (await context.Documents
            .Where(d => clientUserIds.Contains(d.UserId) && d.UploadedAtUtc >= monthStartUtc && d.UploadedAtUtc < monthEndUtc)
            .Select(d => new { d.UserId, d.Category, d.Status })
            .ToListAsync(cancellationToken))
            .Select(d => (d.UserId, d.Category, d.Status))
            .ToList();

        // Get Bolt integrations
        var boltIntegrations = (await context.BoltIntegrations
            .Where(bi => clientUserIds.Contains(bi.UserId))
            .Select(bi => bi.UserId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        // Linked bank connections waive the monthly bank-statement upload
        var linkedBankUsers = (await context.BankConnections
            .Where(bc => clientUserIds.Contains(bc.UserId) && bc.Status == Domain.Banking.BankConnectionStatus.Linked)
            .Select(bc => bc.UserId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        // Get monthly incomes for current month
        Dictionary<Guid, PfaMonthlyIncome> monthlyIncomes = await context.PfaMonthlyIncomes
            .Where(i => pfaIds.Contains(i.PfaRegistrationId) && i.Year == currentYear && i.Month == currentMonth)
            .ToDictionaryAsync(i => i.PfaRegistrationId, cancellationToken);

        int missingMonthlyDocs = 0;
        int readyToProcess = 0;
        int processedThisMonth = 0;

        foreach ((Guid Id, Guid UserId) pfa in pfas)
        {
            monthlyIncomes.TryGetValue(pfa.Id, out PfaMonthlyIncome? income);
            bool isProcessed = income?.IsProcessed ?? false;

            if (isProcessed)
            {
                processedThisMonth++;
                continue;
            }

            var clientDocs = monthDocs
                .Where(d => d.UserId == pfa.UserId)
                .ToList();

            bool isBlocked = clientDocs.Any(d => d.Status == DocumentStatus.Rejected);
            bool hasPendingDocs = clientDocs.Any(d => d.Status == DocumentStatus.Pending);

            bool hasBoltIntegration = boltIntegrations.Contains(pfa.UserId);
            decimal venitUber = income?.VenitUber ?? 0m;
            decimal venitBolt = income?.VenitBolt ?? 0m;

            bool isUberMandatory = venitUber > 0 || venitUber == 0 && venitBolt == 0 && !hasBoltIntegration;
            bool isBoltMandatory = venitBolt > 0 || hasBoltIntegration;

            bool hasLinkedBank = linkedBankUsers.Contains(pfa.UserId);
            bool missingExtras = !hasLinkedBank && !clientDocs.Any(d => d.Category == DocumentCategory.ExtrasBancar);
            bool missingUber = isUberMandatory && !clientDocs.Any(d => d.Category == DocumentCategory.RaportUber);
            bool missingBolt = isBoltMandatory && !clientDocs.Any(d => d.Category == DocumentCategory.RaportBolt);

            bool hasMissingDocs = missingExtras || missingUber || missingBolt;

            if (hasMissingDocs)
            {
                missingMonthlyDocs++;
            }

            bool hasIncome = income is not null;
            bool allVerified = clientDocs.All(d => d.Status == DocumentStatus.Verified);

            if (hasIncome && !isBlocked && !hasPendingDocs && !hasMissingDocs && allVerified)
            {
                readyToProcess++;
            }
        }

        // 6. Mesaje necitite
        int unreadMessages = await context.ChatMessages
            .Where(m => m.ChatRoom.ProfessionalUserId == accountantId && m.SenderId != accountantId && !m.IsRead)
            .CountAsync(cancellationToken);

        return new ContabilStatsResponse(
            totalClients,
            docsToVerify,
            missingMonthlyDocs,
            readyToProcess,
            processedThisMonth,
            unreadMessages,
            monthLabel);
    }
}
