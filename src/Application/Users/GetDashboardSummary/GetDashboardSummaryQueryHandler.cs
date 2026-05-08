using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Documents;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.GetDashboardSummary;

internal sealed class GetDashboardSummaryQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetDashboardSummaryQuery, DashboardSummaryResponse>
{
    public async Task<Result<DashboardSummaryResponse>> Handle(
        GetDashboardSummaryQuery query,
        CancellationToken cancellationToken)
    {
        // PFA registration (most recent)
        PfaRegistration? pfa = await context.PfaRegistrations
            .AsNoTracking()
            .Where(p => p.UserId == query.UserId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        // All documents for this user
        List<Document> documents = await context.Documents
            .AsNoTracking()
            .Where(d => d.UserId == query.UserId)
            .OrderByDescending(d => d.UploadedAtUtc)
            .ToListAsync(cancellationToken);

        int total = documents.Count;
        int verified = documents.Count(d => d.Status == DocumentStatus.Verified);
        int pending = documents.Count(d => d.Status == DocumentStatus.Pending);
        int rejected = documents.Count(d => d.Status == DocumentStatus.Rejected);

        // Unread notifications
        int unread = await context.Notifications
            .Where(n => n.UserId == query.UserId && !n.IsRead)
            .CountAsync(cancellationToken);

        // 5 most recently uploaded documents
        var recent = documents
            .Take(5)
            .Select(d => new RecentDocumentDto(
                d.Id,
                d.OriginalFileName,
                d.Category.ToString(),
                d.Status.ToString(),
                d.UploadedAtUtc))
            .ToList();

        return new DashboardSummaryResponse(
            pfa?.Status.ToString(),
            pfa?.RegistrationType.ToString(),
            pfa?.Cui,
            documents.FirstOrDefault(d => d.Category == DocumentCategory.CertificatInregistrare)?.Id,
            pfa?.CreatedAtUtc,
            total,
            verified,
            pending,
            rejected,
            unread,
            recent);
    }
}
