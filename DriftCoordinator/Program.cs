using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var peers = new ConcurrentDictionary<string, PeerInfo>();
var routingIndex = 0; // Индекс для циклической маршрутизации
var routingLock = new object();

app.MapPost("/register", (PeerInfo peer) =>
{
    peers[peer.Id] = peer;
    Console.WriteLine($"Registered peer: {peer.Id} ({peer.Ip}:{peer.Port})");
    return Results.Ok();
});

app.MapGet("/peers", () => peers.Values.ToList());

app.MapGet("/random-peers", (int count) =>
{
    var rnd = new Random();
    var shuffled = peers.Values.OrderBy(x => rnd.Next()).Take(count).ToList();
    return Results.Ok(shuffled);
});

// === Новый эндпоинт для получения следующего узла в цепочке ===
app.MapGet("/next-peer", () =>
{
    lock (routingLock)
    {
        if (peers.IsEmpty)
        {
            return Results.NotFound("No peers available");
        }

        var peerList = peers.Values.ToList();
        var nextPeer = peerList[routingIndex % peerList.Count];
        routingIndex = (routingIndex + 1) % peerList.Count;

        Console.WriteLine($"Routing to peer: {nextPeer.Id} ({nextPeer.Ip}:{nextPeer.Port}) [index: {routingIndex}]");
        return Results.Ok(nextPeer);
    }
});

// === Эндпоинт для получения случайного узла (для обратной совместимости) ===
app.MapGet("/random-peer", () =>
{
    if (peers.IsEmpty)
    {
        return Results.NotFound("No peers available");
    }

    var rnd = new Random();
    var peer = peers.Values.OrderBy(x => rnd.Next()).First();
    return Results.Ok(peer);
});

app.Run("http://0.0.0.0:8000");

record PeerInfo(string Id, string Ip, int Port);