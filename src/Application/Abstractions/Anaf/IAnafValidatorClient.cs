using Domain.Accounting;
using SharedKernel;

namespace Application.Abstractions.Anaf;

/// <summary>Un mesaj al validatorului ANAF (DUKIntegrator), cum îl întoarce serviciul.</summary>
public sealed record AnafValidatorMessage(string? Code, string Message, string? Field, string? Location);

/// <summary>Răspunsul <c>ridelance-anaf-validator</c>. <see cref="Pdf"/> există doar pentru un XML valid.</summary>
public sealed record AnafValidatorResult(
    bool Valid,
    IReadOnlyList<AnafValidatorMessage> Errors,
    IReadOnlyList<AnafValidatorMessage> Warnings,
    string RawOutput,
    byte[]? Pdf,
    long DurationMs,
    string? CorrelationId);

/// <summary>
/// Nivelul 3 de validare: serviciul intern <c>ridelance-anaf-validator</c> (mod
/// <c>VALIDATE_AND_PDF</c>). Un eșec (<see cref="Result.IsFailure"/>) înseamnă că serviciul nu a
/// putut fi folosit, nu că XML-ul e invalid.
/// </summary>
public interface IAnafValidatorClient
{
    /// <param name="declarationType">Codul declarației din kitul ANAF (<c>D100</c>, <c>D700</c>).</param>
    Task<Result<AnafValidatorResult>> ValidateAsync(
        string declarationType,
        string validatorVersion,
        byte[] xml,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>Declarațiile lunare au tipul lor; D700 (cererea de cod TVA) nu e una dintre ele.</summary>
    Task<Result<AnafValidatorResult>> ValidateAsync(
        DeclarationType type,
        string validatorVersion,
        byte[] xml,
        string correlationId,
        CancellationToken cancellationToken) =>
        ValidateAsync(type.ToString(), validatorVersion, xml, correlationId, cancellationToken);
}
