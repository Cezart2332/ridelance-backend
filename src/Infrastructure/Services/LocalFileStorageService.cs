using Application.Abstractions.Services;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

internal sealed class LocalFileStorageService(IOptions<FileStorageSettings> settings) : IFileStorageService
{
    public async Task<string> SaveAsync(Stream data, string relativePath, CancellationToken cancellationToken)
    {
        string fullPath = Path.Combine(settings.Value.BasePath, relativePath);

        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        await using FileStream fileStream = new(fullPath, FileMode.Create, FileAccess.Write);
        await data.CopyToAsync(fileStream, cancellationToken);

        return fullPath;
    }

    public Task<Stream> ReadAsync(string relativePath, CancellationToken cancellationToken)
    {
        string fullPath = Path.Combine(settings.Value.BasePath, relativePath);

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        string fullPath = Path.Combine(settings.Value.BasePath, relativePath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }
}
