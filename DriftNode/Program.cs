using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DriftNode;

// === Временное хранилище для перехвата чанков ===
// Используется только когда узел работает в режиме восстановления
Dictionary<string, byte[]> TempChunkStore = [];
bool isRecoveryMode = false;

// === Конфигурация узла ===
var coordinatorUrl = Environment.GetEnvironmentVariable("COORDINATOR_URL") ?? "http://localhost:8000";
var nodeId = Environment.GetEnvironmentVariable("NODE_ID") ?? Guid.NewGuid().ToString();
var port = int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "5000");
var sendDelayMinMs = int.Parse(Environment.GetEnvironmentVariable("SEND_DELAY_MIN_MS") ?? "10");
var sendDelayMaxMs = int.Parse(Environment.GetEnvironmentVariable("SEND_DELAY_MAX_MS") ?? "50");
var recentIdsMax = int.Parse(Environment.GetEnvironmentVariable("RECENT_IDS_MAX") ?? "500");
var recentIdTtlSec = int.Parse(Environment.GetEnvironmentVariable("RECENT_ID_TTL_SEC") ?? "10");

if (sendDelayMaxMs < sendDelayMinMs)
{
    // Swap if misconfigured
    (sendDelayMinMs, sendDelayMaxMs) = (sendDelayMaxMs, sendDelayMinMs);
}

var recentChunkCache = new RecentChunkCache(recentIdsMax, TimeSpan.FromSeconds(recentIdTtlSec));

Console.WriteLine($"[INIT] DriftNode {nodeId} starting on port {port}");
Console.WriteLine($"[CONFIG] DELAY {sendDelayMinMs}-{sendDelayMaxMs} ms, CACHE size {recentIdsMax}, TTL {recentIdTtlSec}s");

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
            var chunkId = firstLine[4..]; // Убираем "GET:"
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

            // Anti-loop: drop if already seen
            if (recentChunkCache.Seen(chunkId))
            {
                Console.WriteLine($"[LOOP] dropped {chunkId}");
                // Поглощаем входящий payload, чтобы корректно закрыть соединение
                reader.ReadBytes(payloadSize);
                return;
            }
            recentChunkCache.Add(chunkId);

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
                await ForwardChunkAsync(chunkId, ttl, payload, coordinatorUrl, sendDelayMinMs, sendDelayMaxMs, nodeId);
            }
            else
            {
                // НЕ сохраняем чанк в памяти - только пересылаем дальше
                if (ttl <= 1)
                {
                    Console.WriteLine($"[DROPPED] Chunk {chunkId} expired (TTL={ttl})");
                    // Поглощаем payload, чтобы корректно закрыть соединение
                    reader.ReadBytes(payloadSize);
                    return;
                }

                            // ДОБАВЛЯЕМ ЦИРКУЛЯЦИЮ: Сначала сохраняем payload, потом пересылаем
            var payload = reader.ReadBytes(payloadSize);
            
            // Пересылаем чанк дальше
            await ForwardChunkAsync(chunkId, ttl, payload, coordinatorUrl, sendDelayMinMs, sendDelayMaxMs, nodeId);
            
            // Запускаем постоянную циркуляцию в фоне
            if (ttl > 10) // Только если TTL ещё высокое
            {
                _ = Task.Run(async () =>
                {
                    var currentTtl = ttl - 1;
                    while (currentTtl > 1)
                    {
                        await Task.Delay(Random.Shared.Next(2000, 5000)); // Задержка 2-5 сек
                        
                        try
                        {
                            await ForwardChunkAsync(chunkId, currentTtl, payload, coordinatorUrl, sendDelayMinMs, sendDelayMaxMs, nodeId);
                            currentTtl--;
                            Console.WriteLine($"[CIRCULATE] {chunkId} continuing circulation (TTL={currentTtl})");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[CIRCULATE_ERROR] {chunkId}: {ex.Message}");
                            break;
                        }
                    }
                    Console.WriteLine($"[CIRCULATE_END] {chunkId} circulation ended");
                });
            }
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
static async Task ForwardChunkStreamAsync(string chunkId, int ttl, int payloadSize, BinaryReader reader, string coordinatorUrl, int sendDelayMinMs, int sendDelayMaxMs, string selfId)
{
    using var httpClient = new HttpClient();
    try
    {
        // Используем новый эндпоинт для получения следующего узла в цепочке
        var response = await httpClient.GetStringAsync($"{coordinatorUrl}/next-peer");
        var peer = JsonSerializer.Deserialize<PeerInfo>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Skip if coordinator routed to ourselves – try again up to 3 times
        int attempts = 0;
        while (peer is not null && peer.Id == selfId && attempts < 3)
        {
            response = await httpClient.GetStringAsync($"{coordinatorUrl}/next-peer");
            peer = JsonSerializer.Deserialize<PeerInfo>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            attempts++;
        }

        if (peer is null || peer.Id == selfId)
        {
            Console.WriteLine("[WARN] No other peer available (only myself)");
            return;
        }

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Parse(peer.Ip), peer.Port);

        using var stream = client.GetStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        // Записываем заголовок
        writer.Write(chunkId);
        // Уменьшаем TTL на 1 для предотвращения бесконечных петель
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

        Console.WriteLine($"[FORWARDED] Chunk {chunkId} → {peer.Ip}:{peer.Port} (TTL={ttl-1}) via stream");

        // Rate-limit
        var delayMs = Random.Shared.Next(sendDelayMinMs, sendDelayMaxMs + 1);
        Console.WriteLine($"[DELAY] {delayMs}ms before next forward");
        await Task.Delay(delayMs);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FORWARD_ERROR] {ex.Message}");
    }
}

// === Пересылка чанка из памяти (для режима восстановления) ===
static async Task ForwardChunkAsync(string chunkId, int ttl, byte[] payload, string coordinatorUrl, int sendDelayMinMs, int sendDelayMaxMs, string selfId)
{
    using var httpClient = new HttpClient();
    try
    {
        // Используем новый эндпоинт для получения следующего узла в цепочке
        var response = await httpClient.GetStringAsync($"{coordinatorUrl}/next-peer");
        var peer = JsonSerializer.Deserialize<PeerInfo>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Retry selection if routed to self
        int attempts = 0;
        while (peer is not null && peer.Id == selfId && attempts < 3)
        {
            response = await httpClient.GetStringAsync($"{coordinatorUrl}/next-peer");
            peer = JsonSerializer.Deserialize<PeerInfo>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            attempts++;
        }

        if (peer is null || peer.Id == selfId)
        {
            Console.WriteLine("[WARN] No other peer available (only myself)");
            return;
        }

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Parse(peer.Ip), peer.Port);

        using var stream = client.GetStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(chunkId);
        // Уменьшаем TTL на 1 для предотвращения бесконечных петель
        writer.Write(ttl - 1);
        writer.Write(payload.Length);
        writer.Write(payload);
        writer.Flush();

        Console.WriteLine($"[FORWARDED] Chunk {chunkId} → {peer.Ip}:{peer.Port} (TTL={ttl-1})");

        // Rate-limit
        var delayMs = Random.Shared.Next(sendDelayMinMs, sendDelayMaxMs + 1);
        Console.WriteLine($"[DELAY] {delayMs}ms before next forward");
        await Task.Delay(delayMs);
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
