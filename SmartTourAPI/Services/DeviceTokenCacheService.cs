using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;

namespace SmartTourAPI.Services;

public interface IDeviceTokenCacheService
{
    Task<(string token, DateTime expiresAtUtc)> GetOrCreateAsync(
        string deviceId,
        Func<IdentityUser, Task<(string token, DateTime expiresAtUtc)>> factory);
}

public class DeviceTokenCacheService : IDeviceTokenCacheService
{
    private const int MaxEntries = 1_000_000;
    private const int CleanupEveryCalls = 5_000;

    private readonly ConcurrentDictionary<string, TokenCacheEntry> _cache = new();
    private long _counter;

    public async Task<(string token, DateTime expiresAtUtc)> GetOrCreateAsync(
        string deviceId,
        Func<IdentityUser, Task<(string token, DateTime expiresAtUtc)>> factory)
    {
        if (_cache.TryGetValue(deviceId, out var cached) &&
            cached.ExpiresAtUtc > DateTime.UtcNow.AddSeconds(60))
        {
            return (cached.Token, cached.ExpiresAtUtc);
        }

        var pseudoUser = new IdentityUser
        {
            Id = $"device:{deviceId}",
            UserName = deviceId,
            Email = "device@smarttour.local"
        };

        var created = await factory(pseudoUser);
        _cache[deviceId] = new TokenCacheEntry(created.token, created.expiresAtUtc);

        MaybeCleanup();
        return created;
    }

    private void MaybeCleanup()
    {
        var count = Interlocked.Increment(ref _counter);
        if (count % CleanupEveryCalls != 0)
            return;

        var now = DateTime.UtcNow;
        foreach (var pair in _cache)
        {
            if (pair.Value.ExpiresAtUtc <= now || _cache.Count > MaxEntries)
                _cache.TryRemove(pair.Key, out _);
        }
    }

    private sealed record TokenCacheEntry(string Token, DateTime ExpiresAtUtc);
}
