using System.Text.Json;
using Application.Abstractions.Data;
using Application.Abstractions.Settings;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.AppSettings;

/// <summary>
/// Cache proces-wide (60s) peste tabelul app_settings. Serviciul e scoped ca să poată
/// citi din DbContext, dar cache-ul e static ca să nu lovim baza la fiecare request.
/// </summary>
internal sealed class AppSettingsService(IApplicationDbContext context) : IAppSettings
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private static volatile Snapshot _snapshot = new(new Dictionary<string, string>(StringComparer.Ordinal), DateTime.MinValue);

    public async Task<T> GetAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default)
    {
        Snapshot snapshot = await GetSnapshotAsync(context, cancellationToken);

        if (!snapshot.Values.TryGetValue(key, out string? valueJson) || string.IsNullOrWhiteSpace(valueJson))
        {
            return defaultValue;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(valueJson) ?? defaultValue;
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }

    private static async Task<Snapshot> GetSnapshotAsync(IApplicationDbContext context, CancellationToken cancellationToken)
    {
        Snapshot current = _snapshot;
        if (DateTime.UtcNow < current.ExpiresAtUtc)
        {
            return current;
        }

        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (DateTime.UtcNow < _snapshot.ExpiresAtUtc)
            {
                return _snapshot;
            }

            Dictionary<string, string> rows = await context.AppSettings
                .AsNoTracking()
                .ToDictionaryAsync(s => s.Key, s => s.ValueJson, StringComparer.Ordinal, cancellationToken);

            var fresh = new Snapshot(rows, DateTime.UtcNow.Add(CacheTtl));
            _snapshot = fresh;
            return fresh;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private sealed record Snapshot(IReadOnlyDictionary<string, string> Values, DateTime ExpiresAtUtc);
}
