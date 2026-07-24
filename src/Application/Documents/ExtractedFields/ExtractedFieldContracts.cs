namespace Application.Documents.ExtractedFields;

/// <summary>Un câmp extras, așa cum e expus către client/admin (valorile sensibile sunt redactate).</summary>
public sealed record ExtractedFieldDto(
    Guid Id,
    string FieldKey,
    string? AiValue,
    string? ConfirmedValue,
    string? EffectiveValue,
    double EffectiveConfidence,
    bool ValidatorPassed,
    bool IsSensitive,
    string ReviewState,
    string ConfirmedSource);

public sealed record ExtractedFieldsResponse(
    Guid DocumentId,
    string Category,
    double? OverallConfidence,
    bool RequiresManualReview,
    IReadOnlyList<ExtractedFieldDto> Fields);
