using System.Text.Json.Serialization;
using SharedKernel;

namespace Domain.Accounting;

// Enumerările modulului de contabilitate PFA (spec contabilitate §3). În baza de date se stochează
// ca text (numele C#); în API ies ca UPPER_SNAKE_CASE, exact ca în contractul TypeScript.

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<Platform>))]
public enum Platform
{
    Bolt = 0,
    Uber = 1,
}

/// <summary>§3.1</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<PlatformDocumentStatus>))]
public enum PlatformDocumentStatus
{
    Uploaded = 0,
    Extracting = 1,
    ExtractionFailed = 2,
    NeedsReview = 3,
    PendingConfirmation = 4,
    Confirmed = 5,
    Locked = 6,
}

/// <summary>Clasificarea documentului; vine din extracție.</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<PlatformDocumentType>))]
public enum PlatformDocumentType
{
    Unknown = 0,
    CommissionInvoice = 1,
    PlatformReport = 2,
}

/// <summary>§3.2</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<DeclarationStatus>))]
public enum DeclarationStatus
{
    NotApplicable = 0,
    BlockedMissingDocuments = 1,
    BlockedNeedsReview = 2,
    Draft = 3,
    Generated = 4,
    ValidationFailed = 5,
    Validated = 6,
    ReadyToSign = 7,
#pragma warning disable CA1720 // „Signed” e statusul din contract (SIGNED), nu tipul numeric.
    Signed = 8,
#pragma warning restore CA1720
    Submitted = 9,
    Accepted = 10,
    Rejected = 11,
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<DeclarationType>))]
public enum DeclarationType
{
    D100 = 0,
    D301 = 1,
    D390 = 2,
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<DeclarationVersionKind>))]
public enum DeclarationVersionKind
{
    Initial = 0,
    Rectificative = 1,
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<ValidationLevel>))]
public enum ValidationLevel
{
    Ridelance = 0,
    Xsd = 1,
    Anaf = 2,
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<D100RuleCode>))]
public enum D100RuleCode
{
    D100CommissionNonresident = 0,

    /// <summary>DE CONFIRMAT: dezactivată până la confirmarea regulii (Decizii, pct. 5).</summary>
    D100RentIndividual = 1,
}

/// <summary>§3.3</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<LedgerSource>))]
public enum LedgerSource
{
    Bank = 0,
    Uber = 1,
    Bolt = 2,
    Oblio = 3,
    Upload = 4,
    CashZ = 5,
    Manual = 6,
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<LedgerTransactionType>))]
public enum LedgerTransactionType
{
    Income = 0,
    Expense = 1,
    Transfer = 2,
    OwnerContribution = 3,
    Loan = 4,
    Tax = 5,
    Other = 6,
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<PaymentMethod>))]
public enum PaymentMethod
{
    Bank = 0,
    Cash = 1,
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<DeductibilityType>))]
public enum DeductibilityType
{
    [JsonStringEnumMemberName("100_PERCENT")]
    Percent100 = 0,

    [JsonStringEnumMemberName("50_PERCENT")]
    Percent50 = 1,

    NonDeductible = 2,

    /// <summary>Amortizare și alte cazuri speciale: DE CONFIRMAT, nu prin procentul auto.</summary>
    SpecialRule = 3,
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<LedgerEntryStatus>))]
public enum LedgerEntryStatus
{
    AutoImported = 0,
    NeedsReview = 1,
    Verified = 2,
    Locked = 3,
}

/// <summary>§3.4</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<CashRegisterStatus>))]
public enum CashRegisterStatus
{
    NotRequiredCurrentConfiguration = 0,
    Pending = 1,
    InVerification = 2,
    Active = 3,
}

/// <summary>§3.5</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<AccountingPeriodStatus>))]
public enum AccountingPeriodStatus
{
    Open = 0,
    Closed = 1,
}

/// <summary>§3.6</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<RefStatus>))]
public enum RefStatus
{
    Current = 0,
    Final = 1,
    Intermediate = 2,
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<EngagementStatus>))]
public enum EngagementStatus
{
    Active = 0,
    Inactive = 1,
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<AssetStatus>))]
public enum AssetStatus
{
    InUse = 0,
    Disposed = 1,
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<BackgroundJobStatus>))]
public enum BackgroundJobStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<BackgroundJobType>))]
public enum BackgroundJobType
{
    ProcessPeriod = 0,
    GenerateDeclarations = 1,
    ValidateDeclarations = 2,
    HandoverPackage = 3,
}

/// <summary>Starea unui PFA pe o lună, după pre-check (B3). Alimentează statisticile lunii.</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<PfaMonthStatus>))]
public enum PfaMonthStatus
{
    NotProcessed = 0,
    Ready = 1,
    NeedsReview = 2,
    MissingDocuments = 3,
}

/// <summary>Verificările deterministe de pe documentele platformă (B1).</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<DocumentCheckCode>))]
public enum DocumentCheckCode
{
    AmountInText = 0,
    Arithmetic = 1,
    SupplierKnown = 2,
    VatIdFormat = 3,
    PeriodMatch = 4,
    NotDuplicate = 5,
    NotAlreadyDeclared = 6,
    CurrencyAllowed = 7,
    SettlementCorrelation = 8,
}

/// <summary>Acțiunea propusă lângă o verificare picată.</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<DocumentCheckAction>))]
public enum DocumentCheckAction
{
    AddSupplier = 0,
}

/// <summary>Acțiunile din <c>POST /declaration-versions/{id}/transitions</c> (§4.4).</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<DeclarationAction>))]
public enum DeclarationAction
{
    Validate = 0,
    MarkSigned = 1,
    MarkSubmitted = 2,
    MarkRejected = 3,
    Regenerate = 4,
}
