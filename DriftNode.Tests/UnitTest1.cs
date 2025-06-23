using System.Threading.Tasks;
using Xunit;

namespace DriftNode.Tests;

public class UnitTest1
{
    // removed
}

public class RecentChunkCacheTests
{
    [Fact]
    public void DuplicateChunk_ShouldBeDetected()
    {
        var cache = new DriftNode.RecentChunkCache(200, TimeSpan.FromSeconds(5));

        const string chunkId = "xyz";
        Assert.False(cache.Seen(chunkId));
        cache.Add(chunkId);
        Assert.True(cache.Seen(chunkId));

        // Add again should still be seen
        Assert.True(cache.Seen(chunkId));
    }

    [Fact]
    public async Task ChunkId_ShouldExpire_AfterTtl()
    {
        var ttl = TimeSpan.FromSeconds(1);
        var cache = new DriftNode.RecentChunkCache(200, ttl);
        const string chunkId = "expirable";

        cache.Add(chunkId);
        Assert.True(cache.Seen(chunkId));

        // Wait for TTL + a buffer to allow sweeper to run (1s period)
        await Task.Delay(ttl + TimeSpan.FromSeconds(1));

        Assert.False(cache.Seen(chunkId));
    }
}