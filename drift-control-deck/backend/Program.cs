using System.Diagnostics;
using System.Net;
using Docker.DotNet;
using Microsoft.Data.Sqlite;
using DriftControlDeck.Services;
using DriftControlDeck.Models;

var builder = WebApplication.CreateBuilder(args);

// Configuration & Services
builder.Configuration.AddEnvironmentVariables();
var cfg = builder.Configuration;

builder.Services.AddSingleton<IDockerClient>(_ =>
{
    // Use unix socket by default (Linux/macOS). Adjust for Windows if needed.
    return new Docker.DotNet.DockerClientConfiguration(new Uri("unix:///var/run/docker.sock"))
        .CreateClient();
});

builder.Services.AddSingleton<DockerComposeGenerator>();
builder.Services.AddSingleton<DockerService>();
builder.Services.AddSingleton<MetricsService>();
builder.Services.AddSingleton<ProfileService>();

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Dev-Auth middleware
app.Use(async (ctx, next) =>
{
    var token = cfg["API_TOKEN"];
    if (!string.IsNullOrEmpty(token))
    {
        if (!ctx.Request.Headers.TryGetValue("x-api-token", out var sent) || sent != token)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }
    }
    await next();
});

app.MapPost("/api/launch", async (LaunchRequest req, DockerService docker) =>
{
    await docker.LaunchAsync(req.Nodes, req.Env ?? new());
    return Results.Ok();
});

app.MapPatch("/api/scale", async (ScaleRequest req, DockerService docker) =>
{
    await docker.ScaleAsync(req.Delta);
    return Results.Ok();
});

app.MapGet("/api/metrics", async (MetricsService metrics) =>
{
    var data = await metrics.GetMetricsAsync();
    return Results.Ok(data);
});

app.MapPost("/api/cmd", async (CmdRequest req, DockerService docker) =>
{
    await docker.SendCommandAsync(req.NodeIds, req.Command);
    return Results.Ok();
});

app.MapPost("/api/upload", async (HttpRequest request, DockerService docker) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("multipart expected");
    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file is null) return Results.BadRequest("file missing");
    var ttl = int.Parse(form["ttl"].FirstOrDefault() ?? "3600");
    var copies = int.Parse(form["copies"].FirstOrDefault() ?? "3");
    var id = await docker.UploadAsync(file, ttl, copies);
    return Results.Ok(new { streamId = id });
});

app.MapGet("/api/download/{streamId}", async (string streamId, DockerService docker) =>
{
    var stream = await docker.DownloadAsync(streamId);
    return Results.Stream(stream, "application/octet-stream", $"{streamId}.bin");
});

// Profile persistence
app.MapGet("/api/profiles", (ProfileService svc) => svc.GetAll());
app.MapPost("/api/profiles", (Profile profile, ProfileService svc) => svc.Create(profile));
app.MapPut("/api/profiles/{id}", (string id, Profile profile, ProfileService svc) => svc.Update(id, profile));
app.MapDelete("/api/profiles/{id}", (string id, ProfileService svc) => svc.Delete(id));

// WebSocket metrics push
app.MapGet("/ws/metrics", async (HttpContext ctx, MetricsService metricsSvc) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    await metricsSvc.StreamMetricsAsync(socket, ctx.RequestAborted);
});

app.Run(); 