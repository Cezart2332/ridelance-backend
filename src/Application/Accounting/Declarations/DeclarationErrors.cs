using Application.Accounting.Contracts;
using Domain.Accounting;
using SharedKernel;

namespace Application.Accounting.Declarations;

/// <summary>Erorile declarațiilor. Codurile corespund celor din mock-ul frontendului.</summary>
internal static class DeclarationErrors
{
    public static readonly Error DeclarationNotFound = Error.NotFound("Accounting.DeclarationNotFound", "Declarația nu există.");

    public static readonly Error VersionNotFound = Error.NotFound("Accounting.DeclarationVersionNotFound", "Versiunea declarației nu există.");

    public static readonly Error NotCurrentVersion = Error.Conflict(
        "Accounting.NotCurrentVersion",
        "Doar versiunea curentă își poate schimba statusul. Versiunile vechi rămân neschimbate.");

    public static Error FileMissing(string what) => Error.NotFound("Accounting.FileMissing", $"{what} nu există pentru această versiune.");

    public static Error InvalidTransition(DeclarationStatus status, DeclarationAction action) => Error.Conflict(
        "Accounting.InvalidTransition",
        $"Acțiunea {AccountingJson.Serialize(action).Trim('"')} nu e permisă din statusul {AccountingJson.Serialize(status).Trim('"')}.");
}
