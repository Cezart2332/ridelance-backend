namespace Application.Expenses;

/// <param name="DocumentStatus">
/// Verificarea documentului de către RIDElance: `Pending`, `Verified`, `Rejected`. Separat de
/// <paramref name="Status"/>, care e confirmarea utilizatorului și singura care decide dacă
/// cheltuiala intră în profitul real estimat.
/// </param>
public sealed record DeductibleExpenseResponse(
    Guid Id,
    Guid DocumentId,
    Guid UserId,
    Guid PfaRegistrationId,
    string CatalogCategory,
    string ItemName,
    string DeductibleLabel,
    decimal? AmountRon,
    int Year,
    int Month,
    string DocumentStatus,
    string OriginalFileName,
    long FileSize,
    DateTime UploadedAtUtc,
    DateTime CreatedAtUtc,
    Guid CreatedByUserId,
    DateOnly? ExpenseDate,
    string? SupplierName,
    decimal? VatAmount,
    string Currency,
    string? DocumentTypeLabel,
    string Source,
    string Status);
