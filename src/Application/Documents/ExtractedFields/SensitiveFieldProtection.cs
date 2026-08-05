using Application.Abstractions.Security;
using Domain.Documents;
using Domain.PfaRegistrations.CompanyFormation;

namespace Application.Documents.ExtractedFields;

/// <summary>
/// Regula de stocare a câmpurilor marcate <c>Sensitive</c> în catalogul AI: în coloanele
/// obișnuite ajunge doar masca de afișare, iar valoarea reală stă criptată în
/// <see cref="ExtractedField.EncryptedValue"/>.
///
/// Fără asta, un CNP citit din carte de identitate ar rămâne în clar în <c>ai_value</c> —
/// flagul de sensibil maschează doar răspunsurile API, nu și ce se scrie în baza de date.
/// </summary>
internal static class SensitiveFieldProtection
{
    /// <summary>Masca afișată în locul valorii reale.</summary>
    public static string Mask(ExtractedFieldType type, string value)
    {
        string trimmed = value.Trim();

        return type switch
        {
            ExtractedFieldType.Cnp => CnpValidator.Mask(trimmed),
            _ => trimmed.Length <= 4 ? "••••" : $"••••{trimmed[^4..]}",
        };
    }

    /// <summary>
    /// Scrie o valoare nou extrasă (OCR) pe rând, respectând sensibilitatea. Întoarce ce
    /// trebuie pus în coloanele de afișare.
    /// </summary>
    public static string StoreOcrValue(
        ExtractedField row,
        ExtractedFieldSpecSensitivity sensitivity,
        string normalized,
        ISecretProtector protector)
    {
        if (!sensitivity.IsSensitive)
        {
            row.EncryptedValue = null;
            return normalized;
        }

        row.EncryptedValue = protector.Protect(normalized);
        return Mask(sensitivity.Type, normalized);
    }

    /// <summary>Valoarea în clar a unui câmp sensibil, pentru afișarea la cerere în admin.</summary>
    public static string? Reveal(ExtractedField row, ISecretProtector protector) =>
        string.IsNullOrWhiteSpace(row.EncryptedValue)
            ? row.ConfirmedValue ?? row.AiNormalizedValue
            : protector.Unprotect(row.EncryptedValue);
}

/// <summary>
/// Cât știe stratul de protecție despre un câmp. Un record mic în loc de dependința pe
/// <c>ExtractedFieldSpec</c>, ca regula să fie folosibilă și când specul lipsește.
/// </summary>
internal readonly record struct ExtractedFieldSpecSensitivity(bool IsSensitive, ExtractedFieldType Type);
