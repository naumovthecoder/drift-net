using System.Text.Json.Serialization;

namespace DriftAnalytics.Models;

public class NetworkMetrics
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<ChunkEvent> ChunkEvents { get; set; } = new();
    public List<NodeMetrics> NodeMetrics { get; set; } = new();
    public NetworkStats NetworkStats { get; set; } = new();
}

public class ChunkEvent
{
    public string ChunkId { get; set; } = string.Empty;
    public string SourceNode { get; set; } = string.Empty;
    public string TargetNode { get; set; } = string.Empty;
    public int TTL { get; set; }
    public int Size { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public EventType Type { get; set; }
}

public enum EventType
{
    Received,
    Forwarded,
    Expired
}

public class NodeMetrics
{
    public string NodeId { get; set; } = string.Empty;
    public int ChunksReceived { get; set; }
    public int ChunksForwarded { get; set; }
    public int TotalBytesProcessed { get; set; }
    public double AverageTTL { get; set; }
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}

public class NetworkStats
{
    public int TotalChunksInFlight { get; set; }
    public int TotalBytesInFlight { get; set; }
    public double AverageChunkTTL { get; set; }
    public int ActiveNodes { get; set; }
    public Dictionary<string, int> ChunkDistribution { get; set; } = new();
    public List<ChunkPath> ChunkPaths { get; set; } = new();
}

public class ChunkPath
{
    public string ChunkId { get; set; } = string.Empty;
    public List<string> Path { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int TotalHops { get; set; }
} 