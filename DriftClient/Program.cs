using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;

// === Конфигурация ===
var coordinatorUrl = Environment.GetEnvironmentVariable("COORDINATOR_URL") ?? "http://localhost:8000";
var chunkSize = int.Parse(Environment.GetEnvironmentVariable("CHUNK_SIZE") ?? "256000");
var ttl = int.Parse(Environment.GetEnvironmentVariable("TTL") ?? "10000");

Console.WriteLine("DriftNet Client - P2P File Network Client");
Console.WriteLine("Type 'help' for available commands. Type 'exit' to quit.\n");

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) continue;
    var cmdArgs = ParseArgs(input);
    var command = cmdArgs[0].ToLower();

    switch (command)
    {
        case "upload":
            if (cmdArgs.Length < 2)
            {
                Console.WriteLine("[ERROR] Upload command requires file path");
                Console.WriteLine("Usage: upload <file-path>");
                break;
            }
            await UploadFile(cmdArgs[1]);
            break;
        case "get":
            if (cmdArgs.Length < 2)
            {
                Console.WriteLine("[ERROR] Get command requires chunk prefix");
                Console.WriteLine("Usage: get <chunk-prefix> [--out <file>]");
                break;
            }
            var outputFile = "output.txt";
            for (int i = 2; i < cmdArgs.Length - 1; i++)
            {
                if (cmdArgs[i] == "--out" && i + 1 < cmdArgs.Length)
                {
                    outputFile = cmdArgs[i + 1];
                    break;
                }
            }
            await GetFile(cmdArgs[1], outputFile);
            break;
        case "help":
            PrintHelp();
            break;
        case "exit":
        case "quit":
            Console.WriteLine("Bye!");
            return;
        default:
            Console.WriteLine($"[ERROR] Unknown command: {command}");
            PrintHelp();
            break;
    }
}

void PrintHelp()
{
    Console.WriteLine("Available commands:");
    Console.WriteLine("  upload <file-path>                    - Upload file to network");
    Console.WriteLine("  get <chunk-prefix> [--out <file>]     - Recover file from network");
    Console.WriteLine("  help                                  - Show this help");
    Console.WriteLine("  exit                                  - Quit client");
}

string[] ParseArgs(string input)
{
    var args = new List<string>();
    var sb = new StringBuilder();
    bool inQuotes = false;
    foreach (var c in input)
    {
        if (c == '"') inQuotes = !inQuotes;
        else if (char.IsWhiteSpace(c) && !inQuotes)
        {
            if (sb.Length > 0) { args.Add(sb.ToString()); sb.Clear(); }
        }
        else sb.Append(c);
    }
    if (sb.Length > 0) args.Add(sb.ToString());
    return args.ToArray();
}

// === Функция загрузки файла ===
async Task UploadFile(string filePath)
{
    if (!File.Exists(filePath))
    {
        Console.WriteLine($"[ERROR] File not found: {filePath}");
        return;
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

    // === Получение ВСЕХ пиров ===
    using var httpClient = new HttpClient();
    List<PeerInfo> allPeers = new();

    try
    {
        var response = await httpClient.GetStringAsync($"{coordinatorUrl}/peers");
        allPeers = JsonSerializer.Deserialize<List<PeerInfo>>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<PeerInfo>();
        
        if (allPeers.Count == 0)
        {
            Console.WriteLine("[ERROR] No peers available");
            return;
        }
        
        Console.WriteLine($"[PEERS] Found {allPeers.Count} peers - will use ALL nodes for continuous flow");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Failed to get peers: {ex.Message}");
        return;
    }

    // === Отправка чанков на 3 случайных узла каждый ===
    var random = new Random();
    var tasks = new List<Task>();

    for (int i = 0; i < totalChunks; i++)
    {
        var chunkId = $"chunk-{i + 1:D4}";
        var startIndex = i * chunkSize;
        var endIndex = Math.Min(startIndex + chunkSize, fileBytes.Length);
        var chunkData = fileBytes[startIndex..endIndex];
        
        Console.WriteLine($"[UPLOAD] {chunkId} ({chunkData.Length} bytes)");
        
        // Выбираем 3 случайных узла для каждого чанка
        var selectedPeers = allPeers.OrderBy(x => random.Next()).Take(3).ToList();
        
        // Отправляем каждый чанк только на 3 случайных узла
        var chunkTasks = selectedPeers.Select(async peer =>
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

    Console.WriteLine($"[SENT] All {totalChunks} chunks to 3 random peers each");
    Console.WriteLine("[UPLOAD] Upload completed successfully");
    Console.WriteLine("[INFO] Chunks will now flow between nodes automatically");
}

// === Функция восстановления файла ===
async Task GetFile(string chunkPrefix, string outputFile)
{
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
            return;
        }
        
        Console.WriteLine($"[PEERS] Found {peers.Count} peers");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Failed to get peers: {ex.Message}");
        return;
    }

    // === Включение режима восстановления на всех узлах ===
    Console.WriteLine("[RECOVERY] Enabling recovery mode on all nodes...");
    var recoveryTasks = peers.Select(async peer =>
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Parse(peer.Ip), peer.Port);
            
            using var stream = client.GetStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            
            writer.Write("RECOVERY:");
            writer.Flush();
            
            var response = reader.ReadString();
            Console.WriteLine($"[RECOVERY] Node {peer.Ip}:{peer.Port} - {response}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Failed to enable recovery on {peer.Ip}:{peer.Port}: {ex.Message}");
        }
    });

    await Task.WhenAll(recoveryTasks);
    Console.WriteLine("[RECOVERY] Recovery mode enabled on all nodes");

    // === Ожидание для перехвата чанков ===
    Console.WriteLine("[WAIT] Waiting 5 seconds for chunks to be intercepted...");
    await Task.Delay(5000);

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

    // === Выключение режима восстановления на всех узлах ===
    Console.WriteLine("[NORMAL] Disabling recovery mode on all nodes...");
    var normalTasks = peers.Select(async peer =>
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Parse(peer.Ip), peer.Port);
            
            using var stream = client.GetStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            
            writer.Write("NORMAL:");
            writer.Flush();
            
            var response = reader.ReadString();
            Console.WriteLine($"[NORMAL] Node {peer.Ip}:{peer.Port} - {response}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Failed to disable recovery on {peer.Ip}:{peer.Port}: {ex.Message}");
        }
    });

    await Task.WhenAll(normalTasks);
    Console.WriteLine("[NORMAL] Normal mode enabled on all nodes");

    // === Сохранение результата ===
    if (chunks.Count == 0)
    {
        Console.WriteLine("[ERROR] No chunks found");
        return;
    }

    try
    {
        // Создаем директорию если её нет
        var outputDir = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

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
    }
}

// === Типы данных ===
record PeerInfo(string Id, string Ip, int Port); 