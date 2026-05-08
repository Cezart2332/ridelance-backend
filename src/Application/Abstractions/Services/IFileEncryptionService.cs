namespace Application.Abstractions.Services;

public sealed record EncryptedFileResult(string FilePath, string Iv);

public interface IFileEncryptionService
{
    Task<EncryptedFileResult> EncryptAndSaveAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken);

    Task<Stream> DecryptAndReadAsync(
        string encryptedFilePath,
        string iv,
        CancellationToken cancellationToken);
}
