using Domain.Documents;

namespace Application.Documents.ExtractedFields;

internal static class ExtractedFieldMapper
{
    public static ExtractedFieldDto ToDto(ExtractedField f)
    {
        // Valorile sensibile nu se întorc în clar (nici OCR, nici confirmate).
        string? aiValue = f.IsSensitive ? Mask(f.AiNormalizedValue ?? f.AiValue) : (f.AiNormalizedValue ?? f.AiValue);
        string? confirmed = f.IsSensitive ? Mask(f.ConfirmedValue) : f.ConfirmedValue;
        string? effective = f.IsSensitive ? Mask(f.ConfirmedValue ?? f.AiNormalizedValue) : (f.ConfirmedValue ?? f.AiNormalizedValue);

        return new ExtractedFieldDto(
            f.Id,
            f.FieldKey,
            aiValue,
            confirmed,
            effective,
            f.EffectiveConfidence,
            f.ValidatorPassed,
            f.IsSensitive,
            f.ReviewState.ToString(),
            f.ConfirmedSource.ToString());
    }

    private static string? Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Length <= 4 ? "••••" : $"••••{trimmed[^4..]}";
    }
}
