using System.Net.WebSockets;
using System.Text.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DriftControlDeck.Services;

public class MetricsService
{
    private readonly ILogger<MetricsService> _logger;

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

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect metrics");
            return Array.Empty<object>();
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

        foreach (var line in lines)
        {
            // [RECEIVED] Chunk chunk-0001 | TTL=9998 | Size=90 bytes
            if (line.Contains("[RECEIVED]"))
            {
                metrics.ReceivedChunks++;
                
                // Извлекаем TTL
                var ttlMatch = Regex.Match(line, @"TTL=(\d+)");
                if (ttlMatch.Success && int.TryParse(ttlMatch.Groups[1].Value, out var ttl))
                {
                    ttlValues.Add(ttl);
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