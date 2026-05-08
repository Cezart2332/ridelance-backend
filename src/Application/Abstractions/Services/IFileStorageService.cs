namespace Application.Abstractions.Services;

public interface IFileStorageService
{
    Task<string> SaveAsync(Stream data, string relativePath, CancellationToken cancellationToken);
    Task<Stream> ReadAsync(string relativePath, CancellationToken cancellationToken);
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken);
}
