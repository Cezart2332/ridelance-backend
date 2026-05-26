namespace Application.Expenses;

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
    string Status,
    string OriginalFileName,
    long FileSize,
    DateTime UploadedAtUtc,
    DateTime CreatedAtUtc,
    Guid CreatedByUserId);
