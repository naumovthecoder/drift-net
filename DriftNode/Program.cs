using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

// === In-memory хранилище чанков ===
Dictionary<string, byte[]> MemoryStore = new();

// === Конфигурация узла ===
var coordinatorUrl = Environment.GetEnvironmentVariable("COORDINATOR_URL") ?? "http://localhost:8000";
var nodeId = Environment.GetEnvironmentVariable("NODE_ID") ?? Guid.NewGuid().ToString();
var port = int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "5000");

Console.WriteLine($"[INIT] DriftNode {nodeId} starting on port {port}");

// === Регистрация в координаторе ===
using var httpClient = new HttpClient();

await httpClient.PostAsJsonAsync($"{coordinatorUrl}/register", new
{
    Id = nodeId,
    Ip = GetLocalIpAddress(),
    Port = port
});

Console.WriteLine($"[REGISTERED] at coordinator {coordinatorUrl}");

// === TCP-сервер для приёма чанков ===
var listener = new TcpListener(IPAddress.Any, port);
listener.Start();
Console.WriteLine($"[LISTENING] Node {nodeId} on port {port}");

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(() => HandleClient(client, coordinatorUrl, MemoryStore));
}

// === Обработка входящего соединения ===
async Task HandleClient(TcpClient client, string coordinatorUrl, Dictionary<string, byte[]> memoryStore)
{
    try
    {
        using var stream = client.GetStream();
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        // Читаем первую строку для определения типа запроса
        var firstLine = reader.ReadString();
        
        if (firstLine.StartsWith("GET:"))
        {
            // GET-запрос для получения чанка
            var chunkId = firstLine.Substring(4); // Убираем "GET:"
            await HandleGetRequest(writer, chunkId, memoryStore);
        }
        else
        {
            // Обычный запрос для сохранения чанка
            var chunkId = firstLine;
            var ttl = reader.ReadInt32();
            var payloadSize = reader.ReadInt32();
            var payload = reader.ReadBytes(payloadSize);

            Console.WriteLine($"[RECEIVED] Chunk {chunkId} | TTL={ttl} | Size={payload.Length} bytes");

            // Сохраняем чанк в памяти
            memoryStore[chunkId] = payload;
            Console.WriteLine($"[STORED] Chunk {chunkId} in memory");

            if (ttl <= 0)
            {
                Console.WriteLine($"[DROPPED] Chunk {chunkId} expired");
                return;
            }

            await SendToRandomPeerAsync(chunkId, ttl, payload, coordinatorUrl);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] {ex.Message}");
    }
}

// === Обработка GET-запроса ===
async Task HandleGetRequest(BinaryWriter writer, string chunkId, Dictionary<string, byte[]> memoryStore)
{
    Console.WriteLine($"[GET] Request for chunk {chunkId}");
    
    if (memoryStore.TryGetValue(chunkId, out var payload))
    {
        writer.Write(payload.Length);
        writer.Write(payload);
        writer.Flush();
        Console.WriteLine($"[FOUND] Chunk {chunkId} ({payload.Length} bytes)");
    }
    else
    {
        writer.Write(0); // payloadLength = 0 означает, что чанк не найден
        writer.Flush();
        Console.WriteLine($"[MISSING] Chunk {chunkId}");
    }
}

// === Пересылка чанка следующему узлу ===
static async Task SendToRandomPeerAsync(string chunkId, int ttl, byte[] payload, string coordinatorUrl)
{
    using var httpClient = new HttpClient();
    try
    {
        var response = await httpClient.GetStringAsync($"{coordinatorUrl}/random-peers?count=1");
        var peers = JsonSerializer.Deserialize<List<PeerInfo>>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (peers is null || peers.Count == 0)
        {
            Console.WriteLine("[WARN] No peers found");
            return;
        }

        var peer = peers.First();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Parse(peer.Ip), peer.Port);

        using var stream = client.GetStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(chunkId);
        writer.Write(ttl - 1);
        writer.Write(payload.Length);
        writer.Write(payload);
        writer.Flush();

        Console.WriteLine($"[FORWARDED] Chunk {chunkId} → {peer.Ip}:{peer.Port} (TTL={ttl - 1})");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FORWARD_ERROR] {ex.Message}");
    }
}

// === Утилита для получения IP узла ===
static string GetLocalIpAddress()
{
    return Dns.GetHostEntry(Dns.GetHostName())
        .AddressList.First(x => x.AddressFamily == AddressFamily.InterNetwork)
        .ToString();
}

// === Структура данных пира ===
record PeerInfo(string Id, string Ip, int Port);
