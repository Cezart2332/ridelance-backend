namespace Application.PfaRegistrations.MonthlyIncome;

public sealed record PfaMonthlyIncomeResponse(
    Guid? Id,
    Guid PfaRegistrationId,
    int Year,
    int Month,
    decimal VenitCash,
    decimal VenitCard,
    decimal VenitBolt,
    decimal VenitUber,
    decimal TaxeEstimate,
    decimal VenitTotal,
    DateTime? UpdatedAtUtc,
    bool IsProcessed,
    DateTime? ProcessedAtUtc,
    Guid? ProcessedByUserId,
    string? ProcessedByUserName);
