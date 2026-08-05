using Domain.Documents;

namespace Application.Documents.ExtractedFields;

internal static class ExtractedFieldMapper
{
    public static ExtractedFieldDto ToDto(ExtractedField f)
    {
        // Câmpurile sensibile sunt deja stocate mascat (vezi SensitiveFieldProtection); valoarea
        // reală trăiește criptată în EncryptedValue și nu iese niciodată prin acest DTO.
        // `Mask` rămâne ca plasă de siguranță pentru rândurile scrise înainte de această regulă.
        string? aiValue = f.IsSensitive ? Mask(f.AiNormalizedValue ?? f.AiValue) : f.AiNormalizedValue ?? f.AiValue;
        string? confirmed = f.IsSensitive ? Mask(f.ConfirmedValue) : f.ConfirmedValue;
        string? effective = f.IsSensitive ? Mask(f.ConfirmedValue ?? f.AiNormalizedValue) : f.ConfirmedValue ?? f.AiNormalizedValue;

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

        // Deja mascat la scriere — a doua mascare ar strica formatul (ex. „1******123456").
        if (trimmed.Contains('•', StringComparison.Ordinal) || trimmed.Contains('*', StringComparison.Ordinal))
        {
            return trimmed;
        }

        return trimmed.Length <= 4 ? "••••" : $"••••{trimmed[^4..]}";
    }
}
