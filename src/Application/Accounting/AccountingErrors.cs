using SharedKernel;

namespace Application.Accounting;

/// <summary>Erorile modulului de contabilitate. Codurile corespund celor din mock-ul frontendului.</summary>
public static class AccountingErrors
{
    public static readonly Error PfaNotFound = Error.NotFound("Accounting.PfaNotFound", "PFA-ul nu există.");

    public static readonly Error DocumentNotFound = Error.NotFound("Accounting.DocumentNotFound", "Documentul nu există.");

    public static readonly Error PfaReadOnly = Error.Conflict(
        "Accounting.PfaReadOnly",
        "Dosarul e inactiv și poate fi doar consultat.");

    public static readonly Error InvalidPeriod = Error.Problem("Accounting.InvalidPeriod", "Perioada trebuie să fie în formatul yyyy-MM.");

    public static readonly Error OnlyPdf = Error.Problem("Accounting.OnlyPdf", "Se acceptă doar fișiere PDF.");

    public static readonly Error FileTooLarge = Error.Problem("Accounting.FileTooLarge", "Fișierul e prea mare.");

    public static readonly Error ReasonRequired = Error.Problem("Accounting.ReasonRequired", "Motivul modificării e obligatoriu.");

    public static readonly Error NoChanges = Error.Problem("Accounting.NoChanges", "Nicio valoare nu s-a schimbat.");

    public static readonly Error NotExtracted = Error.Conflict("Accounting.NotExtracted", "Documentul nu a fost încă citit.");

    public static readonly Error ChecksFailed = Error.Conflict(
        "Accounting.ChecksFailed",
        "Documentul are verificări picate și nu poate fi confirmat.");

    public static Error PeriodClosed(string period) =>
        Error.Conflict("Accounting.PeriodClosed", $"Perioada {period} e închisă. Modificările se fac doar prin corecție controlată.");

    public static Error DocumentLocked(string reason) => Error.Conflict("Accounting.DocumentLocked", reason);

    public static Error InvalidField(string field) => Error.Problem("Accounting.InvalidField", $"Câmpul „{field}” nu are o valoare validă.");

    public static Error NotPendingConfirmation(string status) =>
        Error.Conflict("Accounting.InvalidTransition", $"Documentul nu așteaptă confirmare (status {status}).");
}

/// <summary>
/// Upload duplicat (același hash la același PFA): 409 cu legătura spre documentul existent
/// (spec B1). Endpoint-ul pune <see cref="ExistingDocumentId"/> în răspuns.
/// </summary>
public sealed record DuplicatePlatformDocumentError(Guid ExistingDocumentId, string ExistingFileName)
    : Error("Accounting.DuplicateFile", $"Fișierul a fost deja încărcat ca „{ExistingFileName}”.", ErrorType.Conflict);
