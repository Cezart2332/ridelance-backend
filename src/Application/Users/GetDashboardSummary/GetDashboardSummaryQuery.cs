using Application.Abstractions.Messaging;

namespace Application.Users.GetDashboardSummary;

public sealed record GetDashboardSummaryQuery(Guid UserId) : IQuery<DashboardSummaryResponse>;

public sealed record DashboardSummaryResponse(
    // PFA Status
    string? PfaStatus,
    string? PfaRegistrationType,
    string? PfaCui,
    Guid? PfaCertificatId,
    DateTime? PfaCreatedAtUtc,

    // Documents
    int TotalDocuments,
    int ApprovedDocuments,
    int PendingDocuments,
    int RejectedDocuments,

    // Notifications
    int UnreadNotifications,

    // Recent documents uploaded (last 5)
    List<RecentDocumentDto> RecentDocuments);

public sealed record RecentDocumentDto(
    Guid Id,
    string OriginalFileName,
    string Category,
    string Status,
    DateTime UploadedAtUtc);
