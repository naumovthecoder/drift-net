using System.Net.WebSockets;
using System.Text.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace DriftControlDeck.Services;

public class MetricsService
{
    private readonly ILogger<MetricsService> _logger;
    private readonly ConcurrentDictionary<string, ChunkInfo> _liveChunks = new();
    private readonly ConcurrentDictionary<string, FileInfo> _trackedFiles = new();

    public MetricsService(ILogger<MetricsService> logger)
    {
        _logger = logger;
    }

    public async Task<object> GetMetricsAsync()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("docker", "ps --filter label=com.docker.compose.service=driftnode --format {{.Names}}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var proc = System.Diagnostics.Process.Start(psi)!;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var names = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var tasks = names.Select(async name => await GetNodeMetricsAsync(name));
            var results = await Task.WhenAll(tasks);

            return new
            {
                nodes = results,
                files = await GetFileStatusAsync(),
                chunks = GetLiveChunksStatus()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect metrics");
            return new
            {
                nodes = Array.Empty<object>(),
                files = Array.Empty<object>(),
                chunks = new { total = 0, active = 0 }
            };
        }
    }

    private async Task<object> GetNodeMetricsAsync(string containerName)
    {
        try
        {
            // Получаем логи за последние 2 минуты
            var psi = new ProcessStartInfo("docker", $"logs {containerName} --since 2m")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var proc = Process.Start(psi)!;
            var logs = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            // Парсим метрики из логов
            var metrics = ParseLogMetrics(logs);
            
            return new
            {
                id = containerName,
                cpu = 0, // TODO: можно добавить docker stats
                mem = 0, // TODO: можно добавить docker stats  
                netIO = "-",
                chunks = metrics.ReceivedChunks,
                avgTTL = metrics.AverageTTL,
                loopsDropped = metrics.LoopsDropped,
                forwarded = metrics.ForwardedChunks,
                circulating = metrics.CirculatingChunks
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get metrics for {Container}", containerName);
            return new
            {
                id = containerName,
                cpu = 0,
                mem = 0,
                netIO = "-",
                chunks = 0,
                avgTTL = 0,
                loopsDropped = 0,
                forwarded = 0,
                circulating = 0
            };
        }
    }

    private NodeMetrics ParseLogMetrics(string logs)
    {
        var metrics = new NodeMetrics();
        var lines = logs.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        var ttlValues = new List<int>();
        var now = DateTime.UtcNow;

        foreach (var line in lines)
        {
            // [RECEIVED] Chunk chunk-0001 | TTL=9998 | Size=90 bytes
            if (line.Contains("[RECEIVED]"))
            {
                metrics.ReceivedChunks++;
                
                var currentTtl = 0;
                
                // Извлекаем TTL
                var ttlMatch = Regex.Match(line, @"TTL=(\d+)");
                if (ttlMatch.Success && int.TryParse(ttlMatch.Groups[1].Value, out var ttl))
                {
                    ttlValues.Add(ttl);
                    currentTtl = ttl;
                }

                // Извлекаем chunk ID
                var chunkMatch = Regex.Match(line, @"Chunk (chunk-\d+)");
                if (chunkMatch.Success)
                {
                    var chunkId = chunkMatch.Groups[1].Value;
                    _liveChunks.AddOrUpdate(chunkId, 
                        new ChunkInfo { Id = chunkId, LastSeen = now, TTL = currentTtl },
                        (key, old) => { old.LastSeen = now; old.TTL = currentTtl; return old; });
                }
            }
            
            // [FORWARDED] Chunk chunk-0001 → 172.21.0.20:5000 (TTL=9988)
            else if (line.Contains("[FORWARDED]"))
            {
                metrics.ForwardedChunks++;
            }
            
            // [LOOP] dropped chunk-0001
            else if (line.Contains("[LOOP]"))
            {
                metrics.LoopsDropped++;
            }
            
            // [CIRCULATE] chunk-0001 continuing circulation (TTL=9987)
            else if (line.Contains("[CIRCULATE]"))
            {
                metrics.CirculatingChunks++;
            }
        }

        // Вычисляем средний TTL
        if (ttlValues.Count > 0)
        {
            metrics.AverageTTL = (int)ttlValues.Average();
        }

        return metrics;
    }

    private async Task<object> GetFileStatusAsync()
    {
        // Группируем чанки по файлам
        var fileGroups = _liveChunks.Values
            .GroupBy(c => ExtractFilePrefix(c.Id))
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToList();

        var fileStatus = new List<object>();

        foreach (var group in fileGroups)
        {
            var chunks = group.OrderBy(c => ExtractChunkNumber(c.Id)).ToList();
            var totalChunks = chunks.Count;
            var activeChunks = chunks.Count(c => (DateTime.UtcNow - c.LastSeen).TotalMinutes < 5); // Активные за последние 5 минут
            var avgTTL = chunks.Where(c => c.TTL > 0).Select(c => c.TTL).DefaultIfEmpty(0).Average();
            var recoveryRate = totalChunks > 0 ? (double)activeChunks / totalChunks * 100 : 0;

            fileStatus.Add(new
            {
                id = group.Key,
                totalChunks = totalChunks,
                activeChunks = activeChunks,
                recoveryRate = Math.Round(recoveryRate, 1),
                avgTTL = Math.Round(avgTTL, 0),
                status = recoveryRate >= 90 ? "healthy" : recoveryRate >= 50 ? "degraded" : "critical",
                lastSeen = chunks.Max(c => c.LastSeen)
            });
        }

        return fileStatus.OrderByDescending(f => ((dynamic)f).lastSeen).ToList();
    }

    private object GetLiveChunksStatus()
    {
        var now = DateTime.UtcNow;
        var activeChunks = _liveChunks.Values.Count(c => (now - c.LastSeen).TotalMinutes < 5);
        var totalChunks = _liveChunks.Count;
        
        return new
        {
            total = totalChunks,
            active = activeChunks,
            inactive = totalChunks - activeChunks,
            avgTTL = _liveChunks.Values.Where(c => c.TTL > 0).Select(c => c.TTL).DefaultIfEmpty(0).Average()
        };
    }

    private string ExtractFilePrefix(string chunkId)
    {
        // chunk-0001 -> "file"
        var match = Regex.Match(chunkId, @"^(chunk)-\d+$");
        return match.Success ? "file" : "";
    }

    private int ExtractChunkNumber(string chunkId)
    {
        // chunk-0001 -> 1
        var match = Regex.Match(chunkId, @"chunk-(\d+)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var num) ? num : 0;
    }

    public async Task StreamMetricsAsync(WebSocket socket, CancellationToken token)
    {
        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var metrics = await GetMetricsAsync();
            var json = JsonSerializer.Serialize(metrics);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, token);
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
    }
}

public class NodeMetrics
{
    public int ReceivedChunks { get; set; }
    public int ForwardedChunks { get; set; }
    public int LoopsDropped { get; set; }
    public int CirculatingChunks { get; set; }
    public int AverageTTL { get; set; }
}

public class ChunkInfo
{
    public string Id { get; set; } = "";
    public DateTime LastSeen { get; set; }
    public int TTL { get; set; }
}

public class FileInfo
{
    public string Id { get; set; } = "";
    public int TotalChunks { get; set; }
    public int ActiveChunks { get; set; }
    public DateTime LastActivity { get; set; }
} 