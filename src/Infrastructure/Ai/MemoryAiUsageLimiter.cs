using Application.Abstractions.Ai;
using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure.Ai;

/// <summary>Plafonul de apeluri AI, ținut în memoria procesului.</summary>
/// <remarks>
/// Fereastră fixă, nu glisantă: expirarea se pune o singură dată, la primul apel, și nu se
/// prelungește la fiecare următorul. Altfel cine apasă des n-ar mai ieși niciodată din plafon.
///
/// Numărătorul e un obiect mutabil în cache, nu un întreg rescris: rescrierea intrării ar fi
/// resetat expirarea, adică exact fereastra glisantă pe care n-o vrem.
/// </remarks>
internal sealed class MemoryAiUsageLimiter(IMemoryCache cache) : IAiUsageLimiter
{
    private sealed class Counter
    {
        public int Used { get; set; }
    }

    private static readonly Lock CreationGate = new();

    public bool TryConsume(Guid userId, string feature, int maxCalls, TimeSpan window)
    {
        string key = $"ai-usage:{feature}:{userId}";

        Counter counter;
        lock (CreationGate)
        {
            counter = cache.GetOrCreate(key, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = window;
                return new Counter();
            })!;
        }

        lock (counter)
        {
            if (counter.Used >= maxCalls)
            {
                return false;
            }

            counter.Used++;
            return true;
        }
    }
}
