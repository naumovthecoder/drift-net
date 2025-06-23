using System.Diagnostics;
using Docker.DotNet;
using DriftControlDeck.Models;

namespace DriftControlDeck.Services;

public class DockerService
{
    private readonly IDockerClient _client;
    private readonly DockerComposeGenerator _generator;
    private readonly ILogger<DockerService> _logger;

    public DockerService(IDockerClient client, DockerComposeGenerator generator, ILogger<DockerService> logger)
    {
        _client = client;
        _generator = generator;
        _logger = logger;
    }

    public async Task LaunchAsync(int nodes, IDictionary<string, string> env)
    {
        var composeFile = await _generator.GenerateAsync(nodes, env);
        await RunCliAsync("docker", $"compose -f {composeFile} up --build -d");
    }

    public async Task ScaleAsync(int delta)
    {
        await RunCliAsync("docker", $"compose up -d --scale driftnode={delta}");
    }

    public async Task SendCommandAsync(IEnumerable<string> nodeIds, string command)
    {
        // Placeholder – implement TCP send via docker exec or network connection
        _logger.LogInformation("Would send command {cmd} to {nodes}", command, string.Join(",", nodeIds));
    }

    public async Task<string> UploadAsync(IFormFile file, int ttl, int copies)
    {
        var tempPath = Path.GetTempFileName();
        await using (var fs = File.Create(tempPath))
        {
            await file.CopyToAsync(fs);
        }
        // TODO: copy into drift-client container and invoke DriftUploader
        _logger.LogInformation("Uploaded file {path}", tempPath);
        return Path.GetFileName(tempPath);
    }

    public async Task<Stream> DownloadAsync(string streamId)
    {
        // TODO: exec DriftGetter inside client container
        _logger.LogInformation("Download requested for {id}", streamId);
        return new MemoryStream(); // placeholder
    }

    private static async Task RunCliAsync(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true };
        var proc = Process.Start(psi)!;
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
            throw new Exception($"Command {file} {args} failed: {await proc.StandardError.ReadToEndAsync()}");
    }
} 