namespace DriftControlDeck.Models;

public class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Env { get; set; } = new();
} 