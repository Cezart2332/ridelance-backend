namespace Application.Taxes;

/// <param name="IsOverdue">
/// Calculat pe server, în fusul României. Frontendul nu compară date: browserul are alt ceas
/// și alt fus, iar „termen depășit" e exact genul de afirmație care nu suportă aproximări.
/// </param>
/// <param name="DaysUntilDue">Negativ după termen. Null pentru obligațiile deja plătite.</param>
public sealed record TaxObligationResponse(
    Guid Id,
    string Type,
    string TypeLabel,
    int PeriodYear,
    int PeriodMonth,
    string PeriodLabel,
    decimal AmountDue,
    DateOnly DueDate,
    string Status,
    string StatusLabel,
    bool IsOverdue,
    int? DaysUntilDue,
    Guid? DocumentId,
    string? Note,
    DateTime UpdatedAtUtc);
