using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace SnapTranslate.Services;

public sealed class CachedTranslationService : ITranslationService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private readonly ITranslationService _inner;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public CachedTranslationService(ITranslationService inner)
    {
        _inner = inner;
    }

    public async Task<TranslationResult> TranslateAsync(string text, CancellationToken cancellationToken)
    {
        string normalized = text.Trim();
        if (_cache.TryGetValue(normalized, out CacheEntry? entry) &&
            DateTimeOffset.UtcNow - entry.CreatedAt < CacheTtl)
        {
            return entry.Result;
        }

        TranslationResult result = await _inner.TranslateAsync(normalized, cancellationToken);
        _cache[normalized] = new CacheEntry(DateTimeOffset.UtcNow, result);
        return result;
    }

    private sealed record CacheEntry(DateTimeOffset CreatedAt, TranslationResult Result);
}
