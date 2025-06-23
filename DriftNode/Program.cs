using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

// === Временное хранилище для перехвата чанков ===
// Используется только когда узел работает в режиме восстановления
Dictionary<string, byte[]> TempChunkStore = new();
bool isRecoveryMode = false;

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
    _ = Task.Run(() => HandleClient(client, coordinatorUrl));
}

// === Обработка входящего соединения ===
async Task HandleClient(TcpClient client, string coordinatorUrl)
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
            await HandleGetRequest(writer, chunkId);
        }
        else if (firstLine.StartsWith("RECOVERY:"))
        {
            // Включение режима восстановления
            isRecoveryMode = true;
            Console.WriteLine("[RECOVERY] Mode enabled - will intercept chunks");
            writer.Write("OK");
            writer.Flush();
        }
        else if (firstLine.StartsWith("NORMAL:"))
        {
            // Выключение режима восстановления
            isRecoveryMode = false;
            TempChunkStore.Clear();
            Console.WriteLine("[NORMAL] Mode enabled - streaming only");
            writer.Write("OK");
            writer.Flush();
        }
        else
        {
            // Обычный запрос для пересылки чанка
            var chunkId = firstLine;
            var ttl = reader.ReadInt32();
            var payloadSize = reader.ReadInt32();

            Console.WriteLine($"[RECEIVED] Chunk {chunkId} | TTL={ttl} | Size={payloadSize} bytes");

            // Если включен режим восстановления, временно сохраняем чанк
            if (isRecoveryMode)
            {
                var payload = reader.ReadBytes(payloadSize);
                TempChunkStore[chunkId] = payload;
                Console.WriteLine($"[INTERCEPTED] Chunk {chunkId} for recovery");
                
                if (ttl <= 0)
                {
                    Console.WriteLine($"[DROPPED] Chunk {chunkId} expired");
                    return;
                }

                // Пересылаем чанк дальше
                await ForwardChunkAsync(chunkId, ttl, payload, coordinatorUrl);
            }
            else
            {
                // НЕ сохраняем чанк в памяти - только пересылаем дальше
                if (ttl <= 0)
                {
                    Console.WriteLine($"[DROPPED] Chunk {chunkId} expired");
                    return;
                }

                // Пересылаем чанк дальше через стрим
                await ForwardChunkStreamAsync(chunkId, ttl, payloadSize, reader, coordinatorUrl);
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] {ex.Message}");
    }
}

// === Обработка GET-запроса ===
async Task HandleGetRequest(BinaryWriter writer, string chunkId)
{
    Console.WriteLine($"[GET] Request for chunk {chunkId}");
    
    // Проверяем временное хранилище (только в режиме восстановления)
    if (isRecoveryMode && TempChunkStore.TryGetValue(chunkId, out var payload))
    {
        writer.Write(payload.Length);
        writer.Write(payload);
        writer.Flush();
        Console.WriteLine($"[FOUND] Chunk {chunkId} ({payload.Length} bytes) from temp store");
    }
    else
    {
        writer.Write(0); // payloadLength = 0 означает, что чанк не найден
        writer.Flush();
        Console.WriteLine($"[MISSING] Chunk {chunkId} - not stored in memory");
    }
}

// === Пересылка чанка через стрим без сохранения в памяти ===
static async Task ForwardChunkStreamAsync(string chunkId, int ttl, int payloadSize, BinaryReader reader, string coordinatorUrl)
{
    using var httpClient = new HttpClient();
    try
    {
        // Используем новый эндпоинт для получения следующего узла в цепочке
        var response = await httpClient.GetStringAsync($"{coordinatorUrl}/next-peer");
        var peer = JsonSerializer.Deserialize<PeerInfo>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (peer is null)
        {
            Console.WriteLine("[WARN] No next peer found");
            return;
        }

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Parse(peer.Ip), peer.Port);

        using var stream = client.GetStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        // Записываем заголовок
        writer.Write(chunkId);
        writer.Write(ttl - 1);
        writer.Write(payloadSize);

        // Пересылаем данные чанка напрямую из входящего стрима в исходящий
        var buffer = new byte[8192]; // 8KB буфер
        var remainingBytes = payloadSize;
        
        while (remainingBytes > 0)
        {
            var bytesToRead = Math.Min(buffer.Length, remainingBytes);
            var bytesRead = reader.Read(buffer, 0, bytesToRead);
            
            if (bytesRead == 0) break; // Конец стрима
            
            writer.Write(buffer, 0, bytesRead);
            remainingBytes -= bytesRead;
        }
        
        writer.Flush();

        Console.WriteLine($"[FORWARDED] Chunk {chunkId} → {peer.Ip}:{peer.Port} (TTL={ttl - 1}) via stream");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FORWARD_ERROR] {ex.Message}");
    }
}

// === Пересылка чанка из памяти (для режима восстановления) ===
static async Task ForwardChunkAsync(string chunkId, int ttl, byte[] payload, string coordinatorUrl)
{
    using var httpClient = new HttpClient();
    try
    {
        // Используем новый эндпоинт для получения следующего узла в цепочке
        var response = await httpClient.GetStringAsync($"{coordinatorUrl}/next-peer");
        var peer = JsonSerializer.Deserialize<PeerInfo>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (peer is null)
        {
            Console.WriteLine("[WARN] No next peer found");
            return;
        }

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
