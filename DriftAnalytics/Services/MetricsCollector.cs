using DriftAnalytics.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace DriftAnalytics.Services;

public class MetricsCollector : BackgroundService
{
    private readonly LogParser _logParser;
    private readonly ILogger<MetricsCollector> _logger;
    private readonly string _dockerComposePath;
    private readonly List<NetworkMetrics> _metricsHistory = new();
    private readonly object _lockObject = new();
    
    public MetricsCollector(LogParser logParser, ILogger<MetricsCollector> logger)
    {
        _logParser = logParser;
        _logger = logger;
        _dockerComposePath = Environment.GetEnvironmentVariable("DOCKER_COMPOSE_PATH") ?? ".";
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting metrics collection...");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectMetricsFromDockerLogs();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // Собираем каждые 5 секунд
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting metrics");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
    
    private async Task CollectMetricsFromDockerLogs()
    {
        var nodeNames = Enumerable.Range(1, 20).Select(i => $"node{i}").ToList();
        
        foreach (var nodeName in nodeNames)
        {
            try
            {
                var logs = await GetDockerLogs(nodeName);
                foreach (var logLine in logs)
                {
                    var chunkEvent = _logParser.ParseLogLine(logLine);
                    if (chunkEvent != null)
                    {
                        _logger.LogDebug("Parsed event: {ChunkId} {Type} from {Source} to {Target}", 
                            chunkEvent.ChunkId, chunkEvent.Type, chunkEvent.SourceNode, chunkEvent.TargetNode);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to collect logs from {NodeName}", nodeName);
            }
        }
        
        // Сохраняем текущие метрики
        var currentMetrics = _logParser.GetCurrentMetrics();
        lock (_lockObject)
        {
            _metricsHistory.Add(currentMetrics);
            
            // Ограничиваем историю последними 100 записями
            if (_metricsHistory.Count > 100)
            {
                _metricsHistory.RemoveAt(0);
            }
        }
        
        // Очищаем старые данные
        _logParser.ClearOldData(TimeSpan.FromMinutes(10));
        
        // Логируем статистику
        _logger.LogInformation("Network Stats: {ActiveNodes} nodes, {TotalChunks} chunks, {TotalBytes} bytes, Avg TTL: {AvgTTL:F1}",
            currentMetrics.NetworkStats.ActiveNodes,
            currentMetrics.NetworkStats.TotalChunksInFlight,
            currentMetrics.NetworkStats.TotalBytesInFlight,
            currentMetrics.NetworkStats.AverageChunkTTL);
    }
    
    private async Task<List<string>> GetDockerLogs(string containerName)
    {
        var logs = new List<string>();
        
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "docker";
            process.StartInfo.Arguments = $"logs --tail=50 {containerName}";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            
            process.Start();
            
            while (!process.StandardOutput.EndOfStream)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (!string.IsNullOrEmpty(line))
                {
                    logs.Add(line);
                }
            }
            
            await process.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get logs for container {ContainerName}", containerName);
        }
        
        return logs;
    }
    
    public NetworkMetrics GetCurrentMetrics()
    {
        lock (_lockObject)
        {
            return _metricsHistory.LastOrDefault() ?? new NetworkMetrics();
        }
    }
    
    public List<NetworkMetrics> GetMetricsHistory()
    {
        lock (_lockObject)
        {
            return _metricsHistory.ToList();
        }
    }
    
    public string ExportMetricsAsJson()
    {
        var metrics = GetCurrentMetrics();
        return JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true });
    }
    
    public Dictionary<string, object> GetRealTimeStats()
    {
        var currentMetrics = GetCurrentMetrics();
        
        return new Dictionary<string, object>
        {
            ["activeNodes"] = currentMetrics.NetworkStats.ActiveNodes,
            ["totalChunksInFlight"] = currentMetrics.NetworkStats.TotalChunksInFlight,
            ["totalBytesInFlight"] = currentMetrics.NetworkStats.TotalBytesInFlight,
            ["averageTTL"] = currentMetrics.NetworkStats.AverageChunkTTL,
            ["timestamp"] = currentMetrics.Timestamp,
            ["nodeActivity"] = currentMetrics.NodeMetrics.ToDictionary(
                n => n.NodeId,
                n => new
                {
                    chunksReceived = n.ChunksReceived,
                    chunksForwarded = n.ChunksForwarded,
                    totalBytes = n.TotalBytesProcessed,
                    averageTTL = n.AverageTTL,
                    lastActivity = n.LastActivity
                }
            ),
            ["chunkPaths"] = currentMetrics.NetworkStats.ChunkPaths.Select(p => new
            {
                chunkId = p.ChunkId,
                path = p.Path,
                totalHops = p.TotalHops,
                startTime = p.StartTime
            }).ToList()
        };
    }
} 