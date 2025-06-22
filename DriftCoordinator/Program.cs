using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var peers = new ConcurrentDictionary<string, PeerInfo>();

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

app.Run("http://0.0.0.0:8000");

record PeerInfo(string Id, string Ip, int Port);