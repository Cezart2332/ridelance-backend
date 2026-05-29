using Application.Abstractions.Messaging;

namespace Application.Users.GetDashboardSummary;

public sealed record GetDashboardSummaryQuery(Guid UserId) : IQuery<DashboardSummaryResponse>;

public sealed record DashboardSummaryResponse(
    // PFA Status
    Guid? PfaRegistrationId,
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
    List<RecentDocumentDto> RecentDocuments,

    // Monthly income (current month, set by contabil)
    decimal? VenitCash,
    decimal? VenitCard,
    decimal? VenitBolt,
    decimal? VenitUber,
    decimal? TaxeEstimate,
    decimal? VenitTotal,
    int? IncomeYear,
    int? IncomeMonth,

    // Revenue chart (all months of IncomeYear for user's PFA)
    int RevenueChartYear,
    List<MonthlyRevenuePointDto> MonthlyRevenue,

    // YTD tax breakdown (auto-computed from all months of the year)
    int TaxYear,
    decimal YtdTotalIncome,
    decimal YtdDeductibleExpenses,
    decimal YtdProfit,
    decimal YtdCas,
    decimal YtdCass,
    decimal YtdIncomeTax,
    decimal YtdTotalTax,
    decimal YtdNetIncome,
    List<YtdExpenseDto> YtdExpenses);

public sealed record MonthlyRevenuePointDto(int Month, decimal VenitTotal);

public sealed record RecentDocumentDto(
    Guid Id,
    string OriginalFileName,
    string Category,
    string Status,
    DateTime UploadedAtUtc);

public sealed record YtdExpenseDto(
    Guid Id,
    string ItemName,
    string CatalogCategory,
    string DeductibleLabel,
    decimal? AmountRon,
    int Month,
    string DocumentStatus);
