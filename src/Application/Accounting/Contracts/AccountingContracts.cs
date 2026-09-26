using System.Text.Json;
using Domain.Accounting;

namespace Application.Accounting.Contracts;

// DTO-urile modulului de contabilitate: oglinda C# a contractului TypeScript
// (`src/shared/accounting/api/types.ts` din frontend, spec contabilitate §4). Numele proprietăților
// ies camelCase, enum-urile UPPER_SNAKE_CASE (vezi `UpperSnakeCaseEnumConverter`), datele
// `yyyy-MM-dd`, momentele ISO 8601 UTC. Perioada lunară e text `yyyy-MM`.

public sealed record UserRef(Guid Id, string Name);

public sealed record Paged<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record StoredFileRef(Guid Id, string FileName, string ContentType, long SizeBytes, string Hash);

public sealed record JobRef(Guid JobId);

// ─── §4.1 PFA și dosar ──────────────────────────────────────────────────────────────────────────

public sealed record PfaListItem(
    Guid Id,
    string Name,
    string Cui,
    bool Art317,
    IReadOnlyList<Platform> Platforms,
    EngagementStatus EngagementStatus,
    string CurrentPeriod,
    PfaMonthStatus CurrentMonthStatus,
    CashRegisterStatus CashStatus);

public sealed record EngagementInfo(EngagementStatus Status, DateOnly StartDate, DateOnly? EndDate);

public sealed record CashRegisterStateDto(
    CashRegisterStatus Status,
    bool CashRequested,
    bool CashEnabled,
    DateOnly? ActivationDate,
    UserRef? VerifiedBy,
    StoredFileRef? EvidenceFile);

public sealed record PfaAccountingSummary(
    Guid Id,
    string Name,
    string Cui,
    bool RealSystem,
    bool VatPayer,
    bool Art317,
    DateOnly? Art317ActivationDate,
    IReadOnlyList<Platform> Platforms,
    EngagementInfo Engagement,
    string CurrentPeriod,
    PfaMonthStatus CurrentMonthStatus,
    CashRegisterStateDto Cash,
    bool ReadOnly,
    DateOnly? RetentionUntil);

public sealed record SettingHistoryEntry(
    Guid Id,
    string Key,
    JsonElement Value,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    string Note,
    UserRef ChangedBy,
    DateTime ChangedAt);

public sealed record Art317Setting(bool Enabled, DateOnly? ActivationDate);

public sealed record PfaAccountingSettingsDto(
    Guid PfaId,
    bool RealSystem,
    bool VatPayer,
    Art317Setting Art317,
    IReadOnlyList<Platform> Platforms,
    DeductibilityType VehicleDeductibility,
    CashRegisterStateDto Cash,
    IReadOnlyList<SettingHistoryEntry> History);

/// <summary><c>Field</c> e una din <see cref="PfaAccountingSettingKeys"/>; <c>Value</c> are forma cheii.</summary>
public sealed record SettingsChange(string Field, JsonElement Value, DateOnly ValidFrom, string Note);

public sealed record CashTransitionRequest(CashRegisterStatus To, string Note, Guid? EvidenceDocumentId);

public sealed record CashEvidenceUploadResult(Guid DocumentId);

/// <summary>Răspunsul PFA-ului din onboarding (rol PFA): <c>GET/PUT /me/cash-preference</c>.</summary>
public sealed record CashPreference(bool CashRequested, DateTime AnsweredAt);

public sealed record CashPreferenceRequest(bool CashRequested);

public sealed record DeactivateRequest(DateOnly AccountingEndDate);

public sealed record AuditEntryDto(
    Guid Id,
    string Entity,
    string EntityId,
    string Action,
    JsonElement? Before,
    JsonElement? After,
    string? Reason,
    UserRef User,
    DateTime At);

// ─── §4.2 Documente platformă ───────────────────────────────────────────────────────────────────

public record PlatformDocumentDto
{
    public required Guid Id { get; init; }
    public required Guid PfaId { get; init; }
    public required string Period { get; init; }
    public Platform? Platform { get; init; }
    public required PlatformDocumentType DocumentType { get; init; }
    public required string FileName { get; init; }
    public required PlatformDocumentStatus Status { get; init; }
    public required UserRef UploadedBy { get; init; }
    public required DateTime UploadedAt { get; init; }
}

public record PlatformDocumentListItem : PlatformDocumentDto
{
    /// <summary>Comisionul la facturi, venitul la rapoarte; <c>null</c> înainte de extracție.</summary>
    public decimal? MainAmount { get; init; }
    public string? Currency { get; init; }
    public int FailedChecks { get; init; }
}

public sealed record OtherAmount(string Label, decimal Amount);

public sealed record ExtractedFields(
    string? SupplierName,
    string? SupplierCountry,
    string? SupplierVatId,
    string? InvoiceNumber,
    DateOnly? InvoiceDate,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    string? Currency,
    decimal? Amount,
    decimal? CommissionAmount,
    IReadOnlyList<OtherAmount> OtherAmounts);

public sealed record DocumentExtractionDto(
    int Version,
    ExtractedFields Fields,
    IReadOnlyDictionary<string, string> SourceSnippets,
    double? ModelConfidence,
    string? ModelId,
    string? PromptVersion,
    bool IsManualEdit,
    IReadOnlyList<string> ManuallyEditedFields,
    UserRef? CreatedBy,
    DateTime CreatedAt);

public sealed record DocumentCheck(DocumentCheckCode Code, bool Passed, string Message, DocumentCheckAction? Action);

public sealed record DeclarationReference(
    Guid DeclarationId,
    Guid VersionId,
    DeclarationType Type,
    string Period,
    int VersionNo,
    DeclarationVersionKind Kind,
    DeclarationStatus Status);

public sealed record PlatformDocumentDetail : PlatformDocumentListItem
{
    public required StoredFileRef File { get; init; }
    public DocumentExtractionDto? Extraction { get; init; }
    public required IReadOnlyList<DocumentCheck> Checks { get; init; }
    public required IReadOnlyList<DeclarationReference> IncludedIn { get; init; }
    public string? LockedReason { get; init; }
    public UserRef? ReviewedBy { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public string? ExtractionError { get; init; }
}

/// <summary>Câmpurile trimise sunt doar cele schimbate; <c>Reason</c> e obligatoriu.</summary>
public sealed record UpdateExtractionRequest(JsonElement Fields, string Reason);

public sealed record ConfirmBulkRequest(IReadOnlyList<Guid> Ids);

public sealed record SkippedItem(Guid Id, string Reason);

public sealed record ConfirmBulkResult(IReadOnlyList<Guid> Confirmed, IReadOnlyList<SkippedItem> Skipped);

// ─── §4.3 Luna fiscală ──────────────────────────────────────────────────────────────────────────

public sealed record PeriodStats(int Total, int Ready, int NeedsReview, int MissingDocuments, int NotProcessed);

public sealed record PlatformMonthFigures(decimal? Income, decimal? Commission);

public sealed record DeclarationCell(Guid? DeclarationId, Guid? VersionId, DeclarationStatus? Status, decimal? Amount);

public sealed record OverviewRow(
    Guid PfaId,
    string PfaName,
    string Cui,
    PfaMonthStatus Status,
    IReadOnlyList<string> BlockingReasons,
    PlatformMonthFigures? Bolt,
    PlatformMonthFigures? Uber,
    IReadOnlyDictionary<DeclarationType, DeclarationCell> Declarations);

public sealed record PeriodOverview(string Period, PeriodStats Stats, IReadOnlyList<OverviewRow> Rows);

public sealed record JobResultItem(Guid? PfaId, string? PfaName, string Message);

public sealed record JobProgress(int Done, int Total);

public sealed record JobDto(
    Guid Id,
    BackgroundJobType Type,
    BackgroundJobStatus Status,
    JobProgress Progress,
    IReadOnlyList<JobResultItem> Results,
    IReadOnlyList<JobResultItem> Errors,
    DateTime CreatedAt,
    DateTime? FinishedAt,
    StoredFileRef? File);

// ─── §4.4 Declarații ────────────────────────────────────────────────────────────────────────────

public sealed record DeclarationSummary(
    Guid? DeclarationId,
    Guid PfaId,
    string Period,
    DeclarationType Type,
    DeclarationStatus? Status,
    decimal? Amount,
    Guid? CurrentVersionId,
    int? CurrentVersionNo,
    DeclarationVersionKind? CurrentVersionKind,
    IReadOnlyList<string> BlockingReasons);

public sealed record StatusHistoryEntry(DeclarationStatus? From, DeclarationStatus To, DateTime At, UserRef By, string? Note);

public sealed record ValidationMessage(string? Field, string Text);

public sealed record ValidationLevelResult(ValidationLevel Level, bool Passed, IReadOnlyList<ValidationMessage> Messages);

public sealed record ValidationResult(IReadOnlyList<ValidationLevelResult> Levels, DateTime ValidatedAt, string? ValidatorVersion);

public sealed record DeclarationVersionDto(
    Guid Id,
    Guid DeclarationId,
    int VersionNo,
    DeclarationVersionKind Kind,
    DeclarationStatus Status,
    decimal Amount,
    string? SchemaVersion,
    string? RectificationReason,
    bool HasXml,
    bool HasPdf,
    string? ReceiptNumber,
    StoredFileRef? ReceiptFile,
    IReadOnlyList<StatusHistoryEntry> StatusHistory,
    DateTime CreatedAt,
    string RowVersion);

public sealed record DeclarationDetail(
    Guid Id,
    Guid PfaId,
    string Period,
    DeclarationType Type,
    IReadOnlyList<DeclarationVersionDto> Versions,
    Guid CurrentVersionId);

public sealed record DeclarationLineDto(
    Guid Id,
    Guid SourceDocumentId,
    string SourceDocumentLabel,
    string RuleCode,
    decimal Base,
    decimal? Rate,
    decimal Value,
    string Currency,
    decimal? ExchangeRate,
    string Explanation,
    string SupplierName,
    string SupplierCountry,
    string SupplierVatId,
    string? OperationType,
    string? Treaty,
    DateOnly? ResidenceCertValidFrom,
    DateOnly? ResidenceCertValidTo);

public sealed record DeclarationBreakdown(
    IReadOnlyList<DeclarationLineDto> Lines,
    decimal Total,
    string Explanation,
    decimal? ExcludedRideIncome);

public sealed record TransitionRequest(DeclarationAction Action, string? Note);

public sealed record RectificationRequest(string Reason);

// ─── §4.5 Reguli fiscale ────────────────────────────────────────────────────────────────────────

public sealed record SupplierTaxProfileDto(
    Guid Id,
    string SupplierName,
    string Country,
    string VatId,
    string IncomeType,
    string? Treaty,
    decimal? D100Rate,
    bool D100RateConfirmed,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    DateOnly? ResidenceCertValidFrom,
    DateOnly? ResidenceCertValidTo,
    StoredFileRef? ResidenceCertFile,
    string? Note);

public sealed record VatRateDto(Guid Id, decimal Rate, DateOnly ValidFrom, DateOnly? ValidTo);

public sealed record D100RuleDto(
    Guid Id,
    D100RuleCode Code,
    bool Enabled,
    string Description,
    bool PendingConfirmation,
    JsonElement Parameters,
    DateOnly ValidFrom,
    DateOnly? ValidTo);

public sealed record AnafDeclarationSchemaDto(
    Guid Id,
    DeclarationType DeclarationType,
    string Version,
    StoredFileRef? XsdFile,
    string? ValidatorVersion,
    DateOnly ValidFrom,
    DateOnly? ValidTo);

public sealed record ExpenseCategoryRuleDto(
    Guid Id,
    string Category,
    string Label,
    bool VehicleRelated,
    DeductibilityType DefaultDeductibility,
    string? CounterpartyPattern,
    DateOnly ValidFrom,
    DateOnly? ValidTo);

public sealed record ExchangeRateDto(string Currency, DateOnly Date, decimal Rate, string Source);

// ─── §4.6 Ledger, cash, registre, perioade ──────────────────────────────────────────────────────

public sealed record DeductibilityRuleRef(string? SettingKey, Guid? RuleId, DateOnly ValidFrom);

public sealed record LedgerEntryDto(
    Guid Id,
    Guid PfaId,
    DateOnly Date,
    string DocumentLabel,
    Guid? SourceDocumentId,
    LedgerSource Source,
    string? ExternalId,
    string? Counterparty,
    string Description,
    LedgerTransactionType TransactionType,
    PaymentMethod PaymentMethod,
    decimal Amount,
    string Currency,
    string? Category,
    bool VehicleRelated,
    DeductibilityType? DeductibilityType,
    decimal? DeductiblePercent,
    decimal? DeductibleAmount,
    DeductibilityRuleRef? DeductibilityRule,
    LedgerEntryStatus Status,
    string AccountingPeriod,
    bool ClosedPeriodFlag,
    string RowVersion);

/// <summary>Câmpurile trimise sunt doar cele schimbate; <c>Reason</c> e obligatoriu.</summary>
public sealed record UpdateLedgerEntryRequest(JsonElement Fields, string Reason);

public sealed record ManualLedgerEntryRequest(
    DateOnly Date,
    string DocumentLabel,
    string? Counterparty,
    string Description,
    LedgerTransactionType TransactionType,
    PaymentMethod PaymentMethod,
    decimal Amount,
    string? Category,
    string Reason);

public sealed record ExpenseDocumentExtracted(string? Merchant, string? MerchantCui, DateOnly? Date, decimal? Total, IReadOnlyList<string> Items);

public sealed record ExpenseDocumentUploadResult(Guid DocumentId, ExpenseDocumentExtracted Extracted, LedgerEntryDto? ProposedMatch);

public sealed record ZReportExtracted(DateOnly? Date, string? ZNumber, decimal? Total);

public sealed record ZReportUploadResult(ZReportExtracted Extracted, LedgerEntryDto LedgerEntry);

public sealed record AssetDto(
    Guid Id,
    Guid PfaId,
    string Type,
    string Description,
    DateOnly AcquisitionDate,
    decimal AcquisitionValue,
    StoredFileRef? Document,
    AssetStatus Status,
    DateOnly? DisposedDate);

public sealed record AssetInput(
    string Type,
    string Description,
    DateOnly AcquisitionDate,
    decimal AcquisitionValue,
    AssetStatus Status,
    DateOnly? DisposedDate);

public sealed record RjipRow(Guid LedgerEntryId, DateOnly Date, string Document, string Operation, decimal CashIn, decimal CashOut, decimal BankIn, decimal BankOut);

public sealed record RjipMonthTotal(string Period, decimal CashIn, decimal CashOut, decimal BankIn, decimal BankOut);

public sealed record RjipView(Guid PfaId, DateOnly From, DateOnly To, IReadOnlyList<RjipRow> Rows, IReadOnlyList<RjipMonthTotal> MonthTotals);

public sealed record RefRow(int Year, bool Rectification, string IncomeCategory, string CalculationElement, decimal Value);

public sealed record RefView(Guid PfaId, int Year, RefStatus Status, DateOnly? AsOf, IReadOnlyList<RefRow> Rows);

public sealed record InventoryView(Guid PfaId, int Year, IReadOnlyList<AssetDto> Assets);

public sealed record AccountingPeriodDto(Guid PfaId, string Period, AccountingPeriodStatus Status, UserRef? ClosedBy, DateTime? ClosedAt);

public sealed record PeriodCorrectionRequest(Guid? LedgerEntryId, JsonElement Change, string Reason);

public sealed record PeriodCorrectionDto(Guid Id, Guid PfaId, string Period, Guid? LedgerEntryId, JsonElement Change, string Reason, UserRef By, DateTime At);
