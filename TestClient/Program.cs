using System.Net;
using System.Net.Sockets;
using System.Text;

class Program
{
    static async Task Main(string[] args)
    {
        var nodePort = int.Parse(args.Length > 0 ? args[0] : "5000");
        var chunkId = args.Length > 1 ? args[1] : "test-chunk-001";
        var ttl = int.Parse(args.Length > 2 ? args[2] : "3");
        
        Console.WriteLine($"[TEST] Sending chunk {chunkId} with TTL={ttl} to localhost:{nodePort}");
        
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, nodePort);
            
            using var stream = client.GetStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            
            var payload = Encoding.UTF8.GetBytes($"Test payload for chunk {chunkId}");
            
            writer.Write(chunkId);
            writer.Write(ttl);
            writer.Write(payload.Length);
            writer.Write(payload);
            writer.Flush();
            
            Console.WriteLine($"[TEST] Chunk {chunkId} sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TEST_ERROR] {ex.Message}");
        }
    }
} 