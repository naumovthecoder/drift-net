using System.Collections.Concurrent;
using System.Threading;
using System.Linq;

namespace DriftNode;

/// <summary>
/// Thread-safe in-memory cache that keeps track of the most recently seen chunk IDs.
/// Designed to prevent forwarding duplicates while streaming data between peers.
/// </summary>
public sealed class RecentChunkCache : IDisposable
{
    private readonly int _maxEntries;
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<string, DateTime> _entries = new();
    private readonly Timer _sweeper;

    public RecentChunkCache(int maxEntries, TimeSpan ttl)
    {
        _maxEntries = maxEntries;
        _ttl = ttl;
        // Run sweeper every second as per requirements.
        _sweeper = new Timer(Sweep!, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Checks whether the specified chunk ID has already been seen within TTL window.
    /// </summary>
    public bool Seen(string id) => _entries.ContainsKey(id);

    /// <summary>
    /// Adds the specified chunk ID to the cache, replacing previous timestamp if present.
    /// Entry is timestamped with current UTC time.
    /// </summary>
    public void Add(string id)
    {
        _entries[id] = DateTime.UtcNow;

        // Capacity guard – if we exceed the soft cap, remove oldest records.
        if (_entries.Count <= _maxEntries) return;

        foreach (var outdated in _entries
                     .OrderBy(kvp => kvp.Value)
                     .Take(_entries.Count - _maxEntries))
        {
            _entries.TryRemove(outdated.Key, out _);
        }
    }

    private void Sweep(object state)
    {
        var threshold = DateTime.UtcNow - _ttl;
        foreach (var kvp in _entries)
        {
            if (kvp.Value < threshold)
            {
                _entries.TryRemove(kvp.Key, out _);
            }
        }
    }

    public void Dispose()
    {
        _sweeper.Dispose();
    }
}