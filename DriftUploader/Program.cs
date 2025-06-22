using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

// === Конфигурация ===
var coordinatorUrl = Environment.GetEnvironmentVariable("COORDINATOR_URL") ?? "http://localhost:8000";
var chunkSize = int.Parse(Environment.GetEnvironmentVariable("CHUNK_SIZE") ?? "256000");
var ttl = int.Parse(Environment.GetEnvironmentVariable("TTL") ?? "30");

// === Проверка аргументов ===
if (args.Length == 0)
{
    Console.WriteLine("Usage: drift-upload <file-path>");
    Console.WriteLine("Environment variables:");
    Console.WriteLine("  COORDINATOR_URL - Coordinator URL (default: http://localhost:8000)");
    Console.WriteLine("  CHUNK_SIZE - Chunk size in bytes (default: 256000)");
    Console.WriteLine("  TTL - Time to live for chunks (default: 30)");
    Environment.Exit(1);
}

var filePath = args[0];

if (!File.Exists(filePath))
{
    Console.WriteLine($"[ERROR] File not found: {filePath}");
    Environment.Exit(1);
}

Console.WriteLine($"[UPLOAD] Starting upload of {filePath}");
Console.WriteLine($"[CONFIG] Coordinator: {coordinatorUrl}");
Console.WriteLine($"[CONFIG] Chunk size: {chunkSize} bytes");
Console.WriteLine($"[CONFIG] TTL: {ttl}");

// === Загрузка и разбивка файла ===
var fileBytes = await File.ReadAllBytesAsync(filePath);
var totalChunks = (int)Math.Ceiling((double)fileBytes.Length / chunkSize);

Console.WriteLine($"[INFO] File size: {fileBytes.Length} bytes");
Console.WriteLine($"[INFO] Total chunks: {totalChunks}");

// === Получение случайных пиров ===
using var httpClient = new HttpClient();
List<PeerInfo> peers = new();

try
{
    var response = await httpClient.GetStringAsync($"{coordinatorUrl}/random-peers?count=5");
    peers = JsonSerializer.Deserialize<List<PeerInfo>>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<PeerInfo>();
    
    if (peers.Count == 0)
    {
        Console.WriteLine("[ERROR] No peers available");
        Environment.Exit(1);
    }
    
    Console.WriteLine($"[PEERS] Found {peers.Count} peers:");
    foreach (var peer in peers)
    {
        Console.WriteLine($"  - {peer.Id} ({peer.Ip}:{peer.Port})");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[ERROR] Failed to get peers: {ex.Message}");
    Environment.Exit(1);
}

// === Отправка чанков ===
var tasks = new List<Task>();

for (int i = 0; i < totalChunks; i++)
{
    var chunkId = $"chunk-{i + 1:D4}";
    var startIndex = i * chunkSize;
    var endIndex = Math.Min(startIndex + chunkSize, fileBytes.Length);
    var chunkData = fileBytes[startIndex..endIndex];
    
    Console.WriteLine($"[UPLOAD] {chunkId} ({chunkData.Length} bytes)");
    
    var chunkTasks = peers.Select(async peer =>
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Parse(peer.Ip), peer.Port);
            
            using var stream = client.GetStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            
            writer.Write(chunkId);
            writer.Write(ttl);
            writer.Write(chunkData.Length);
            writer.Write(chunkData);
            writer.Flush();
            
            Console.WriteLine($"[UPLOAD] {chunkId} → {peer.Ip}:{peer.Port}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] connection failed to peer {peer.Id}: {ex.Message}");
        }
    });
    
    tasks.AddRange(chunkTasks);
}

// === Ожидание завершения всех отправок ===
await Task.WhenAll(tasks);

Console.WriteLine($"[SENT] All {totalChunks} chunks to {peers.Count} peers");
Console.WriteLine("[UPLOAD] Upload completed successfully");

// === Типы данных ===
record PeerInfo(string Id, string Ip, int Port); 