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
    private readonly ConcurrentDictionary<string, NodePerformance> _nodePerformance = new();
    private readonly ConcurrentDictionary<string, HotChunk> _hotChunks = new();

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
                chunks = GetLiveChunksStatus(),
                hotChunks = GetHotChunksAnalytics(),
                networkHealth = GetNetworkHealthMetrics(results),
                nodeRankings = GetNodeRankings(results),
                trafficPatterns = GetTrafficPatternsAsync()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect metrics");
            return new
            {
                nodes = Array.Empty<object>(),
                files = Array.Empty<object>(),
                chunks = new { total = 0, active = 0 },
                hotChunks = new { topChunks = Array.Empty<object>(), totalTransfers = 0 },
                networkHealth = new { score = 0, status = "unknown" },
                nodeRankings = Array.Empty<object>(),
                trafficPatterns = new { peakHour = 0, avgThroughput = 0 }
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
            var metrics = ParseLogMetrics(logs, containerName);
            
            return new
            {
                id = containerName,
                cpu = await GetCpuUsageAsync(containerName),
                mem = await GetMemoryUsageAsync(containerName),
                netIO = await GetNetworkIOAsync(containerName),
                chunks = metrics.ReceivedChunks,
                avgTTL = metrics.AverageTTL,
                loopsDropped = metrics.LoopsDropped,
                forwarded = metrics.ForwardedChunks,
                circulating = metrics.CirculatingChunks,
                contributionScore = CalculateContributionScore(metrics),
                uptime = await GetUptimeAsync(containerName),
                errorRate = metrics.ErrorRate,
                bandwidth = metrics.BandwidthMBps
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
                circulating = 0,
                contributionScore = 0,
                uptime = "0m",
                errorRate = 0,
                bandwidth = 0
            };
        }
    }

    private NodeMetrics ParseLogMetrics(string logs, string containerName)
    {
        var metrics = new NodeMetrics();
        var lines = logs.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        var ttlValues = new List<int>();
        var now = DateTime.UtcNow;
        var transfers = new List<DateTime>();

        // Отслеживаем производительность ноды
        var nodePerf = _nodePerformance.GetOrAdd(containerName, new NodePerformance());

        foreach (var line in lines)
        {
            var timestamp = ExtractTimestamp(line);
            transfers.Add(timestamp);

            // [RECEIVED] Chunk chunk-0001 | TTL=9998 | Size=90 bytes
            if (line.Contains("[RECEIVED]"))
            {
                metrics.ReceivedChunks++;
                nodePerf.TotalReceived++;
                
                var currentTtl = 0;
                
                // Извлекаем TTL
                var ttlMatch = Regex.Match(line, @"TTL=(\d+)");
                if (ttlMatch.Success && int.TryParse(ttlMatch.Groups[1].Value, out var ttl))
                {
                    ttlValues.Add(ttl);
                    currentTtl = ttl;
                }

                // Извлекаем размер для расчета bandwidth
                var sizeMatch = Regex.Match(line, @"Size=(\d+) bytes");
                if (sizeMatch.Success && int.TryParse(sizeMatch.Groups[1].Value, out var size))
                {
                    nodePerf.TotalBytesTransferred += size;
                }

                // Извлекаем chunk ID и отслеживаем популярность
                var chunkMatch = Regex.Match(line, @"Chunk (chunk-\d+)");
                if (chunkMatch.Success)
                {
                    var chunkId = chunkMatch.Groups[1].Value;
                    _liveChunks.AddOrUpdate(chunkId, 
                        new ChunkInfo { Id = chunkId, LastSeen = now, TTL = currentTtl },
                        (key, old) => { old.LastSeen = now; old.TTL = currentTtl; return old; });
                    
                    // Отслеживаем горячие чанки
                    _hotChunks.AddOrUpdate(chunkId,
                        new HotChunk { Id = chunkId, TransferCount = 1, LastTransfer = now },
                        (key, old) => { old.TransferCount++; old.LastTransfer = now; return old; });
                }
            }
            
            // [FORWARDED] Chunk chunk-0001 → 172.21.0.20:5000 (TTL=9988)
            else if (line.Contains("[FORWARDED]"))
            {
                metrics.ForwardedChunks++;
                nodePerf.TotalForwarded++;
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

            // [ERROR] или [FAILED] для отслеживания ошибок
            else if (line.Contains("[ERROR]") || line.Contains("[FAILED]"))
            {
                metrics.ErrorCount++;
            }
        }

        // Вычисляем средний TTL
        if (ttlValues.Count > 0)
        {
            metrics.AverageTTL = (int)ttlValues.Average();
        }

        // Рассчитываем bandwidth (MB/s)
        if (transfers.Count > 1)
        {
            var timeSpan = (transfers.Max() - transfers.Min()).TotalSeconds;
            if (timeSpan > 0 && nodePerf.TotalBytesTransferred > 0)
            {
                metrics.BandwidthMBps = Math.Min(1000, nodePerf.TotalBytesTransferred / timeSpan / 1024 / 1024);
            }
        }

        // Рассчитываем error rate
        var totalOperations = metrics.ReceivedChunks + metrics.ForwardedChunks + metrics.CirculatingChunks;
        if (totalOperations > 0)
        {
            metrics.ErrorRate = Math.Min(100, (double)metrics.ErrorCount / totalOperations * 100);
        }

        return metrics;
    }

    private object GetHotChunksAnalytics()
    {
        var topChunks = _hotChunks.Values
            .Where(c => (DateTime.UtcNow - c.LastTransfer).TotalMinutes < 10) // активные за последние 10 минут
            .OrderByDescending(c => c.TransferCount)
            .Take(10)
            .Select(c => new
            {
                id = c.Id,
                transferCount = c.TransferCount,
                lastTransfer = c.LastTransfer,
                popularityScore = CalculatePopularityScore(c)
            })
            .ToList();

        var totalTransfers = _hotChunks.Values.Sum(c => c.TransferCount);

        return new
        {
            topChunks = topChunks,
            totalTransfers = totalTransfers,
            uniqueChunks = _hotChunks.Count,
            avgTransfersPerChunk = _hotChunks.Count > 0 && totalTransfers > 0 ? Math.Round((double)totalTransfers / _hotChunks.Count, 2) : 0
        };
    }

    private object GetNetworkHealthMetrics(object[] nodeResults)
    {
        var nodes = nodeResults.Cast<dynamic>().ToList();
        if (nodes.Count == 0) return new { score = 0, status = "offline" };

        var totalNodes = nodes.Count;
        var activeNodes = nodes.Count(n => n.chunks > 0 || n.forwarded > 0);
        var avgErrorRate = nodes.Average(n => (double)n.errorRate);
        var totalChunksInNetwork = nodes.Sum(n => (int)n.chunks);

        // Вычисляем health score (0-100)
        var activityScore = (double)activeNodes / totalNodes * 40; // 40% веса
        var errorScore = Math.Max(0, 30 - avgErrorRate); // 30% веса (меньше ошибок = лучше)
        var distributionScore = CalculateDistributionScore(nodes) * 30; // 30% веса

        var healthScore = activityScore + errorScore + distributionScore;

        var status = healthScore >= 80 ? "excellent" :
                    healthScore >= 60 ? "good" :
                    healthScore >= 40 ? "fair" :
                    healthScore >= 20 ? "poor" : "critical";

        return new
        {
            score = Math.Round(healthScore, 1),
            status = status,
            activeNodes = activeNodes,
            totalNodes = totalNodes,
            avgErrorRate = Math.Round(avgErrorRate, 2),
            networkLoad = totalChunksInNetwork,
            redundancyLevel = CalculateRedundancyLevel()
        };
    }

    private object[] GetNodeRankings(object[] nodeResults)
    {
        var nodes = nodeResults.Cast<dynamic>().ToList();
        
        return nodes
            .OrderByDescending(n => (double)n.contributionScore)
            .Take(5)
            .Select((n, index) => new
            {
                rank = index + 1,
                nodeId = ((string)n.id).Replace("backend-driftnode-", "node-"),
                contributionScore = Math.Round((double)n.contributionScore, 1),
                totalActivity = (int)n.chunks + (int)n.forwarded + (int)n.circulating,
                efficiency = CalculateEfficiency(n),
                badge = GetPerformanceBadge(index + 1, (double)n.contributionScore)
            })
            .ToArray();
    }

    private object GetTrafficPatternsAsync()
    {
        var now = DateTime.UtcNow;
        var recentActivity = _hotChunks.Values
            .Where(c => (now - c.LastTransfer).TotalHours < 24)
            .GroupBy(c => c.LastTransfer.Hour)
            .Select(g => new { hour = g.Key, transfers = g.Sum(c => c.TransferCount) })
            .OrderByDescending(x => x.transfers)
            .ToList();

        var peakHour = recentActivity.FirstOrDefault()?.hour ?? now.Hour;
        var avgThroughput = recentActivity.Any() ? recentActivity.Average(x => x.transfers) : 0;

        return new
        {
            peakHour = peakHour,
            avgThroughput = Math.Round(avgThroughput, 1),
            hourlyPattern = recentActivity.Take(24).ToArray(),
            trendDirection = CalculateTrendDirection(recentActivity.Cast<dynamic>().ToList())
        };
    }

    // Вспомогательные методы для расчетов
    private double CalculateContributionScore(NodeMetrics metrics)
    {
        // Комплексный score: получение (20%) + форвардинг (40%) + циркуляция (30%) + надежность (10%)
        var receiveScore = Math.Min(metrics.ReceivedChunks * 0.1, 20);
        var forwardScore = Math.Min(metrics.ForwardedChunks * 0.2, 40);
        var circulateScore = Math.Min(metrics.CirculatingChunks * 0.3, 30);
        var reliabilityScore = Math.Max(0, 10 - metrics.ErrorRate);
        
        return receiveScore + forwardScore + circulateScore + reliabilityScore;
    }

    private double CalculatePopularityScore(HotChunk chunk)
    {
        var ageHours = (DateTime.UtcNow - chunk.LastTransfer).TotalHours;
        var ageFactor = Math.Max(0.1, 1 - (ageHours / 24)); // старые чанки теряют популярность
        return chunk.TransferCount * ageFactor;
    }

    private double CalculateDistributionScore(List<dynamic> nodes)
    {
        if (nodes.Count == 0) return 0;
        
        var chunks = nodes.Select(n => (int)n.chunks).ToList();
        var avg = chunks.Average();
        
        if (avg == 0) return 100; // Если у всех нулевые чанки, то распределение "идеальное"
        
        var variance = chunks.Sum(c => Math.Pow(c - avg, 2)) / chunks.Count;
        var stdDev = Math.Sqrt(variance);
        
        // Меньше стандартное отклонение = лучше распределение
        var score = 100 - (stdDev / avg * 100);
        return Math.Max(0, Math.Min(100, score));
    }

    private double CalculateRedundancyLevel()
    {
        if (_liveChunks.Count == 0) return 0;
        
        // Примерный расчет: количество активных нод / общее количество чанков
        var activeNodesCount = _nodePerformance.Count(kv => kv.Value.TotalReceived > 0);
        if (activeNodesCount == 0) return 0;
        
        var redundancy = (double)activeNodesCount / _liveChunks.Count * 100;
        return Math.Max(0, Math.Min(100, redundancy));
    }

    private double CalculateEfficiency(dynamic node)
    {
        var received = (int)node.chunks;
        var forwarded = (int)node.forwarded;
        var errors = (double)node.errorRate;
        
        if (received == 0) return 0;
        var efficiency = (double)forwarded / received * 100 - errors;
        return Math.Max(0, Math.Min(100, efficiency));
    }

    private string GetPerformanceBadge(int rank, double score)
    {
        return rank switch
        {
            1 => score > 80 ? "🏆 MVP" : "🥇 Leader",
            2 => "🥈 High Performer", 
            3 => "🥉 Top Contributor",
            4 => "⭐ Rising Star",
            5 => "💪 Solid Node",
            _ => "📊 Active"
        };
    }

    private string CalculateTrendDirection(List<dynamic> recentActivity)
    {
        if (recentActivity.Count < 2) return "➡️ stable";
        
        var recentItems = recentActivity.Take(6).ToList();
        var olderItems = recentActivity.Skip(6).Take(6).ToList();
        
        if (!recentItems.Any() || !olderItems.Any()) return "➡️ stable";
        
        var recent = recentItems.Average(x => (double)x.transfers);
        var older = olderItems.Average(x => (double)x.transfers);
        
        if (older == 0) return recent > 0 ? "📈 trending up" : "➡️ stable";
        
        if (recent > older * 1.2) return "📈 trending up";
        if (recent < older * 0.8) return "📉 trending down";
        return "➡️ stable";
    }

    // Новые методы для системных метрик
    private async Task<double> GetCpuUsageAsync(string containerName)
    {
        try
        {
            var psi = new ProcessStartInfo("docker", $"stats {containerName} --no-stream --format \"table {{{{.CPUPerc}}}}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var proc = Process.Start(psi)!;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 1)
            {
                var cpuStr = lines[1].Replace("%", "");
                if (double.TryParse(cpuStr, out var cpu))
                    return Math.Round(cpu, 1);
            }
        }
        catch { }
        return 0;
    }

    private async Task<double> GetMemoryUsageAsync(string containerName)
    {
        try
        {
            var psi = new ProcessStartInfo("docker", $"stats {containerName} --no-stream --format \"table {{{{.MemUsage}}}}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var proc = Process.Start(psi)!;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 1)
            {
                // Парсим "123.4MiB / 456.7MiB"
                var memStr = lines[1].Split('/')[0].Trim();
                var value = double.Parse(System.Text.RegularExpressions.Regex.Match(memStr, @"[\d.]+").Value);
                return Math.Round(value, 1);
            }
        }
        catch { }
        return 0;
    }

    private async Task<string> GetNetworkIOAsync(string containerName)
    {
        try
        {
            var psi = new ProcessStartInfo("docker", $"stats {containerName} --no-stream --format \"table {{{{.NetIO}}}}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var proc = Process.Start(psi)!;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 1)
            {
                return lines[1].Trim();
            }
        }
        catch { }
        return "-";
    }

    private async Task<string> GetUptimeAsync(string containerName)
    {
        try
        {
            var psi = new ProcessStartInfo("docker", $"inspect {containerName} --format \"{{{{.State.StartedAt}}}}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var proc = Process.Start(psi)!;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            
            if (DateTime.TryParse(output.Trim(), out var startTime))
            {
                var uptime = DateTime.UtcNow - startTime.ToUniversalTime();
                if (uptime.TotalDays >= 1)
                    return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
                else if (uptime.TotalHours >= 1)
                    return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
                else
                    return $"{(int)uptime.TotalMinutes}m";
            }
        }
        catch { }
        return "0m";
    }

    private DateTime ExtractTimestamp(string logLine)
    {
        // Пытаемся извлечь timestamp из лога или используем текущее время
        return DateTime.UtcNow;
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
        var total = _liveChunks.Count;
        var active = _liveChunks.Values.Count(c => (DateTime.UtcNow - c.LastSeen).TotalMinutes < 5);
        var inactive = total - active;
        var avgTTL = _liveChunks.Values.Where(c => c.TTL > 0).Select(c => c.TTL).DefaultIfEmpty(0).Average();

        return new
        {
            total = total,
            active = active,
            inactive = inactive,
            avgTTL = avgTTL
        };
    }

    private string ExtractFilePrefix(string chunkId)
    {
        // Извлекаем prefix из chunk-0001 -> "file"
        return "file"; // Упрощенная версия
    }

    private int ExtractChunkNumber(string chunkId)
    {
        var match = System.Text.RegularExpressions.Regex.Match(chunkId, @"chunk-(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    public async Task StreamMetricsAsync(WebSocket socket, CancellationToken token)
    {
        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var metrics = await GetMetricsAsync();
            var json = JsonSerializer.Serialize(metrics);
            var buffer = System.Text.Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(buffer, WebSocketMessageType.Text, true, token);
            await Task.Delay(2000, token);
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
    public int ErrorCount { get; set; }
    public double ErrorRate { get; set; }
    public double BandwidthMBps { get; set; }
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

public class NodePerformance
{
    public long TotalReceived { get; set; }
    public long TotalForwarded { get; set; }
    public long TotalBytesTransferred { get; set; }
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}

public class HotChunk
{
    public string Id { get; set; } = "";
    public int TransferCount { get; set; }
    public DateTime LastTransfer { get; set; }
} 