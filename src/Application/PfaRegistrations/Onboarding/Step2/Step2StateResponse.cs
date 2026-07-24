namespace Application.PfaRegistrations.Onboarding.Step2;

public sealed record Step2FiscalDto(string VatAnswer, string VatRegistrationKind);

public sealed record Step2BankDto(
    string? BankName,
    string? IbanMasked,
    bool HasConfirmationDocument,
    bool? OcrIbanMatches,
    string Source,
    string Status);

public sealed record Step2OblioDto(
    string? AccountEmail,
    bool AccountCreationConsent,
    bool DataProcessingConsent,
    bool EInvoiceConsent,
    bool AutoInvoicingConsent,
    bool RidelanceManagementConsent,
    bool TermsAcceptedConsent,
    bool AllConsentsAccepted,
    string IntegrationStatus);

public sealed record Step2SignatureDocDto(string Type, string? Label, bool IsSigned);

public sealed record Step2SignatureDto(
    string Provider,
    string Status,
    IReadOnlyList<Step2SignatureDocDto> Documents);

public sealed record Step2StateResponse(
    Guid? PfaRegistrationId,
    Step2FiscalDto? Fiscal,
    Step2BankDto? Bank,
    Step2OblioDto? Oblio,
    Step2SignatureDto? Signature);
