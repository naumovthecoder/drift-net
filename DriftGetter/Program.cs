using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

// === Конфигурация ===
var coordinatorUrl = Environment.GetEnvironmentVariable("COORDINATOR_URL") ?? "http://localhost:8000";

// === Проверка аргументов ===
if (args.Length == 0)
{
    Console.WriteLine("Usage: drift-get <chunk-prefix> [--out <output-file>]");
    Console.WriteLine("Example: drift-get chunk-");
    Console.WriteLine("Example: drift-get chunk- --out recovered.txt");
    Console.WriteLine("Environment variables:");
    Console.WriteLine("  COORDINATOR_URL - Coordinator URL (default: http://localhost:8000)");
    Environment.Exit(1);
}

var chunkPrefix = args[0];
var outputFile = "output.txt";

// Парсим опцию --out
for (int i = 1; i < args.Length - 1; i++)
{
    if (args[i] == "--out" && i + 1 < args.Length)
    {
        outputFile = args[i + 1];
        break;
    }
}

Console.WriteLine($"[GET] Starting recovery with prefix: {chunkPrefix}");
Console.WriteLine($"[CONFIG] Coordinator: {coordinatorUrl}");
Console.WriteLine($"[CONFIG] Output file: {outputFile}");

// === Получение списка всех пиров ===
using var httpClient = new HttpClient();
List<PeerInfo> peers = new();

try
{
    var response = await httpClient.GetStringAsync($"{coordinatorUrl}/peers");
    peers = JsonSerializer.Deserialize<List<PeerInfo>>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<PeerInfo>();
    
    if (peers.Count == 0)
    {
        Console.WriteLine("[ERROR] No peers available");
        Environment.Exit(1);
    }
    
    Console.WriteLine($"[PEERS] Found {peers.Count} peers");
}
catch (Exception ex)
{
    Console.WriteLine($"[ERROR] Failed to get peers: {ex.Message}");
    Environment.Exit(1);
}

// === Восстановление чанков ===
var chunks = new Dictionary<int, byte[]>();
var consecutiveEmpty = 0;
var chunkIndex = 0;

while (consecutiveEmpty < 10)
{
    var chunkId = $"{chunkPrefix}{chunkIndex:D4}";
    var found = false;
    
    // Пробуем получить чанк от каждого пира
    foreach (var peer in peers)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Parse(peer.Ip), peer.Port);
            
            using var stream = client.GetStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            
            // Отправляем GET-запрос
            writer.Write($"GET:{chunkId}");
            writer.Flush();
            
            // Читаем ответ
            var payloadLength = reader.ReadInt32();
            
            if (payloadLength > 0)
            {
                var payload = reader.ReadBytes(payloadLength);
                chunks[chunkIndex] = payload;
                Console.WriteLine($"[FOUND] {chunkId} from {peer.Ip}:{peer.Port} ({payload.Length} bytes)");
                found = true;
                consecutiveEmpty = 0;
                break;
            }
        }
        catch (Exception ex)
        {
            // Игнорируем ошибки подключения к отдельным пирам
            continue;
        }
    }
    
    if (!found)
    {
        Console.WriteLine($"[MISSING] {chunkId}");
        consecutiveEmpty++;
    }
    
    chunkIndex++;
}

// === Сохранение результата ===
if (chunks.Count == 0)
{
    Console.WriteLine("[ERROR] No chunks found");
    Environment.Exit(1);
}

try
{
    using var fileStream = File.Create(outputFile);
    
    // Записываем чанки в порядке их индексов
    for (int i = 0; i < chunkIndex; i++)
    {
        if (chunks.TryGetValue(i, out var chunkData))
        {
            await fileStream.WriteAsync(chunkData);
        }
    }
    
    Console.WriteLine($"[DONE] Output saved to {outputFile}");
    Console.WriteLine($"[INFO] Recovered {chunks.Count} chunks, total size: {chunks.Values.Sum(c => c.Length)} bytes");
}
catch (Exception ex)
{
    Console.WriteLine($"[ERROR] Failed to save file: {ex.Message}");
    Environment.Exit(1);
}

// === Типы данных ===
record PeerInfo(string Id, string Ip, int Port); 