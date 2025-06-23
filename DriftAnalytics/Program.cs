using DriftAnalytics.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton<LogParser>();
        services.AddSingleton<MetricsCollector>();
        services.AddHostedService<MetricsCollector>(provider => provider.GetRequiredService<MetricsCollector>());
        services.AddHostedService<WebServer>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    });

var host = builder.Build();

// Запускаем сервисы в фоне
await host.StartAsync();

var metricsCollector = host.Services.GetRequiredService<MetricsCollector>();

Console.WriteLine("=== DriftNet Analytics Dashboard ===");
Console.WriteLine("🌐 Web Dashboard: http://localhost:8080");
Console.WriteLine("Type 'help' for available commands. Type 'exit' to quit.");
Console.WriteLine();

// Ждем немного для сбора первых метрик
await Task.Delay(TimeSpan.FromSeconds(10));

while (true)
{
    Console.Write("analytics> ");
    var input = Console.ReadLine()?.Trim().ToLower();
    
    if (string.IsNullOrEmpty(input)) continue;
    
    switch (input)
    {
        case "help":
            ShowHelp();
            break;
            
        case "exit":
            await host.StopAsync();
            return;
            
        case "stats":
            ShowRealTimeStats(metricsCollector);
            break;
            
        case "nodes":
            ShowNodeActivity(metricsCollector);
            break;
            
        case "chunks":
            ShowChunkPaths(metricsCollector);
            break;
            
        case "export":
            ExportMetrics(metricsCollector);
            break;
            
        case "web":
            OpenWebDashboard();
            break;
            
        case "clear":
            Console.Clear();
            break;
            
        default:
            Console.WriteLine($"Unknown command: {input}. Type 'help' for available commands.");
            break;
    }
    
    Console.WriteLine();
}

static void ShowHelp()
{
    Console.WriteLine("Available commands:");
    Console.WriteLine("  stats   - Show real-time network statistics");
    Console.WriteLine("  nodes   - Show node activity and metrics");
    Console.WriteLine("  chunks  - Show chunk paths and distribution");
    Console.WriteLine("  export  - Export current metrics as JSON");
    Console.WriteLine("  web     - Open web dashboard in browser");
    Console.WriteLine("  clear   - Clear console");
    Console.WriteLine("  help    - Show this help");
    Console.WriteLine("  exit    - Exit the application");
}

static void ShowRealTimeStats(MetricsCollector collector)
{
    var stats = collector.GetRealTimeStats();
    
    Console.WriteLine("=== Real-Time Network Statistics ===");
    Console.WriteLine($"Timestamp: {stats["timestamp"]}");
    Console.WriteLine($"Active Nodes: {stats["activeNodes"]}");
    Console.WriteLine($"Total Chunks in Flight: {stats["totalChunksInFlight"]}");
    Console.WriteLine($"Total Bytes in Flight: {FormatBytes((int)stats["totalBytesInFlight"])}");
    Console.WriteLine($"Average TTL: {stats["averageTTL"]:F1}");
}

static void ShowNodeActivity(MetricsCollector collector)
{
    var stats = collector.GetRealTimeStats();
    var nodeActivity = (Dictionary<string, object>)stats["nodeActivity"];
    
    Console.WriteLine("=== Node Activity ===");
    Console.WriteLine($"{"Node",-8} {"Received",-10} {"Forwarded",-10} {"Bytes",-12} {"Avg TTL",-8} {"Last Activity",-20}");
    Console.WriteLine(new string('-', 70));
    
    foreach (var node in nodeActivity.OrderBy(n => n.Key))
    {
        var nodeData = (JsonElement)node.Value;
        var lastActivity = nodeData.GetProperty("lastActivity").GetDateTime();
        
        Console.WriteLine($"{node.Key,-8} " +
                         $"{nodeData.GetProperty("chunksReceived").GetInt32(),-10} " +
                         $"{nodeData.GetProperty("chunksForwarded").GetInt32(),-10} " +
                         $"{FormatBytes(nodeData.GetProperty("totalBytes").GetInt32()),-12} " +
                         $"{nodeData.GetProperty("averageTTL").GetDouble():F1,-8} " +
                         $"{lastActivity:HH:mm:ss}");
    }
}

static void ShowChunkPaths(MetricsCollector collector)
{
    var stats = collector.GetRealTimeStats();
    var chunkPaths = (JsonElement)stats["chunkPaths"];
    
    Console.WriteLine("=== Chunk Paths ===");
    Console.WriteLine($"Active chunks: {chunkPaths.GetArrayLength()}");
    Console.WriteLine();
    
    foreach (var chunk in chunkPaths.EnumerateArray().Take(10)) // Показываем первые 10
    {
        var chunkId = chunk.GetProperty("chunkId").GetString();
        var path = chunk.GetProperty("path");
        var totalHops = chunk.GetProperty("totalHops").GetInt32();
        var startTime = chunk.GetProperty("startTime").GetDateTime();
        
        Console.WriteLine($"Chunk: {chunkId}");
        Console.WriteLine($"  Path: {string.Join(" → ", path.EnumerateArray().Select(n => n.GetString()))}");
        Console.WriteLine($"  Hops: {totalHops}, Started: {startTime:HH:mm:ss}");
        Console.WriteLine();
    }
    
    if (chunkPaths.GetArrayLength() > 10)
    {
        Console.WriteLine($"... and {chunkPaths.GetArrayLength() - 10} more chunks");
    }
}

static void ExportMetrics(MetricsCollector collector)
{
    var json = collector.ExportMetricsAsJson();
    var filename = $"driftnet-metrics-{DateTime.Now:yyyyMMdd-HHmmss}.json";
    
    try
    {
        File.WriteAllText(filename, json);
        Console.WriteLine($"Metrics exported to {filename}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to export metrics: {ex.Message}");
    }
}

static void OpenWebDashboard()
{
    try
    {
        var url = "http://localhost:8080";
        Console.WriteLine($"Opening web dashboard: {url}");
        
        // Попытка открыть браузер
        var process = new System.Diagnostics.Process();
        process.StartInfo.FileName = "open"; // macOS
        process.StartInfo.Arguments = url;
        process.Start();
    }
    catch
    {
        try
        {
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "xdg-open"; // Linux
            process.StartInfo.Arguments = "http://localhost:8080";
            process.Start();
        }
        catch
        {
            Console.WriteLine("Please open http://localhost:8080 in your browser manually.");
        }
    }
}

static string FormatBytes(int bytes)
{
    string[] sizes = { "B", "KB", "MB", "GB" };
    double len = bytes;
    int order = 0;
    
    while (len >= 1024 && order < sizes.Length - 1)
    {
        order++;
        len = len / 1024;
    }
    
    return $"{len:0.#} {sizes[order]}";
} 