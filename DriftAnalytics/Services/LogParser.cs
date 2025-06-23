using DriftAnalytics.Models;
using System.Text.RegularExpressions;

namespace DriftAnalytics.Services;

public class LogParser
{
    private readonly Dictionary<string, ChunkPath> _chunkPaths = new();
    private readonly Dictionary<string, NodeMetrics> _nodeMetrics = new();
    
    public ChunkEvent? ParseLogLine(string logLine)
    {
        // Парсим логи вида:
        // node1 | [RECEIVED] Chunk chunk-0001 | TTL=5623 | Size=256000 bytes
        // node1 | [FORWARDED] Chunk chunk-0001 → 172.19.0.14:5019 (TTL=5599) via stream
        
        var receivedPattern = @"(\w+)\s*\|\s*\[RECEIVED\]\s+Chunk\s+(\w+)\s*\|\s*TTL=(\d+)\s*\|\s*Size=(\d+)\s+bytes";
        var forwardedPattern = @"(\w+)\s*\|\s*\[FORWARDED\]\s+Chunk\s+(\w+)\s*→\s*([\d\.]+):(\d+)\s*\(TTL=(\d+)\)\s+via\s+stream";
        
        var receivedMatch = Regex.Match(logLine, receivedPattern);
        if (receivedMatch.Success)
        {
            var nodeId = receivedMatch.Groups[1].Value;
            var chunkId = receivedMatch.Groups[2].Value;
            var ttl = int.Parse(receivedMatch.Groups[3].Value);
            var size = int.Parse(receivedMatch.Groups[4].Value);
            
            UpdateNodeMetrics(nodeId, chunkId, ttl, size, EventType.Received);
            UpdateChunkPath(chunkId, nodeId, EventType.Received);
            
            return new ChunkEvent
            {
                ChunkId = chunkId,
                SourceNode = "unknown",
                TargetNode = nodeId,
                TTL = ttl,
                Size = size,
                Type = EventType.Received
            };
        }
        
        var forwardedMatch = Regex.Match(logLine, forwardedPattern);
        if (forwardedMatch.Success)
        {
            var nodeId = forwardedMatch.Groups[1].Value;
            var chunkId = forwardedMatch.Groups[2].Value;
            var targetIp = forwardedMatch.Groups[3].Value;
            var targetPort = forwardedMatch.Groups[4].Value;
            var ttl = int.Parse(forwardedMatch.Groups[5].Value);
            
            var targetNode = GetNodeIdFromAddress(targetIp, targetPort);
            UpdateNodeMetrics(nodeId, chunkId, ttl, 0, EventType.Forwarded);
            UpdateChunkPath(chunkId, targetNode, EventType.Forwarded);
            
            return new ChunkEvent
            {
                ChunkId = chunkId,
                SourceNode = nodeId,
                TargetNode = targetNode,
                TTL = ttl,
                Size = 0, // Размер не указан в логе форвардинга
                Type = EventType.Forwarded
            };
        }
        
        return null;
    }
    
    private string GetNodeIdFromAddress(string ip, string port)
    {
        // Маппинг IP:port к nodeId (можно расширить)
        var portNum = int.Parse(port);
        return $"node{portNum - 5000}"; // Предполагаем, что порты начинаются с 5001
    }
    
    private void UpdateNodeMetrics(string nodeId, string chunkId, int ttl, int size, EventType type)
    {
        if (!_nodeMetrics.ContainsKey(nodeId))
        {
            _nodeMetrics[nodeId] = new NodeMetrics { NodeId = nodeId };
        }
        
        var metrics = _nodeMetrics[nodeId];
        metrics.LastActivity = DateTime.UtcNow;
        
        switch (type)
        {
            case EventType.Received:
                metrics.ChunksReceived++;
                metrics.TotalBytesProcessed += size;
                break;
            case EventType.Forwarded:
                metrics.ChunksForwarded++;
                break;
        }
        
        // Обновляем средний TTL
        var totalTTL = metrics.AverageTTL * (metrics.ChunksReceived + metrics.ChunksForwarded - 1) + ttl;
        metrics.AverageTTL = totalTTL / (metrics.ChunksReceived + metrics.ChunksForwarded);
    }
    
    private void UpdateChunkPath(string chunkId, string nodeId, EventType type)
    {
        if (!_chunkPaths.ContainsKey(chunkId))
        {
            _chunkPaths[chunkId] = new ChunkPath
            {
                ChunkId = chunkId,
                StartTime = DateTime.UtcNow
            };
        }
        
        var path = _chunkPaths[chunkId];
        
        if (type == EventType.Received && !path.Path.Contains(nodeId))
        {
            path.Path.Add(nodeId);
            path.TotalHops = path.Path.Count;
        }
    }
    
    public NetworkMetrics GetCurrentMetrics()
    {
        var events = new List<ChunkEvent>();
        var stats = new NetworkStats
        {
            ActiveNodes = _nodeMetrics.Count,
            ChunkPaths = _chunkPaths.Values.ToList()
        };
        
        // Подсчитываем статистику
        foreach (var node in _nodeMetrics.Values)
        {
            stats.TotalChunksInFlight += node.ChunksReceived + node.ChunksForwarded;
            stats.TotalBytesInFlight += node.TotalBytesProcessed;
        }
        
        if (stats.TotalChunksInFlight > 0)
        {
            stats.AverageChunkTTL = _nodeMetrics.Values.Average(n => n.AverageTTL);
        }
        
        return new NetworkMetrics
        {
            ChunkEvents = events,
            NodeMetrics = _nodeMetrics.Values.ToList(),
            NetworkStats = stats
        };
    }
    
    public void ClearOldData(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        
        // Очищаем старые пути чанков
        var oldChunks = _chunkPaths.Where(kvp => kvp.Value.StartTime < cutoff).ToList();
        foreach (var chunk in oldChunks)
        {
            _chunkPaths.Remove(chunk.Key);
        }
        
        // Очищаем неактивные узлы
        var inactiveNodes = _nodeMetrics.Where(kvp => kvp.Value.LastActivity < cutoff).ToList();
        foreach (var node in inactiveNodes)
        {
            _nodeMetrics.Remove(node.Key);
        }
    }
} 