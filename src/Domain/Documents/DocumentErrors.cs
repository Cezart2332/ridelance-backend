using SharedKernel;

namespace Domain.Documents;

public static class DocumentErrors
{
    public static Error NotFound(Guid documentId) => Error.NotFound(
        "Documents.NotFound",
        $"The document with Id = '{documentId}' was not found.");

    public static readonly Error InvalidFileType = Error.Failure(
        "Documents.InvalidFileType",
        "Only PDF, JPG, and PNG files are allowed.");

    public static readonly Error FileTooLarge = Error.Failure(
        "Documents.FileTooLarge",
        "The file exceeds the maximum allowed size of 10 MB.");

    public static readonly Error AccessDenied = Error.Failure(
        "Documents.AccessDenied",
        "You do not have permission to access this document.");
}
