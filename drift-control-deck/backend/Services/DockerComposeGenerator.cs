using System.Text;

namespace DriftControlDeck.Services;

public class DockerComposeGenerator
{
    private readonly IConfiguration _config;
    private readonly string _templatePath;
    private readonly string _outputPath;

    public DockerComposeGenerator(IConfiguration config)
    {
        _config = config;
        _templatePath = _config["ComposeTemplate"] ?? "compose.template.yml";
        _outputPath = _config["ComposeOutput"] ?? "docker-compose.generated.yml";
    }

    public async Task<string> GenerateAsync(int nodes, IDictionary<string, string> env)
    {
        var template = await File.ReadAllTextAsync(_templatePath);
        var compose = template.Replace("${DRIFTNODE_REPLICAS}", nodes.ToString());
        // Very naive env injection – replace tokens of form ${KEY}
        foreach (var kv in env)
        {
            compose = compose.Replace($"${{ENV_{kv.Key}}}", kv.Value);
        }
        await File.WriteAllTextAsync(_outputPath, compose, Encoding.UTF8);
        return _outputPath;
    }
} 