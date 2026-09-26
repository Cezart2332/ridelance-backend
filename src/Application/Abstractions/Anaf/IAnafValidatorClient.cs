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
    Task<Result<AnafValidatorResult>> ValidateAsync(
        DeclarationType type,
        string validatorVersion,
        byte[] xml,
        string correlationId,
        CancellationToken cancellationToken);
}
