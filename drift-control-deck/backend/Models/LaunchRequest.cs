namespace DriftControlDeck.Models;

public record LaunchRequest(int Nodes, Dictionary<string, string>? Env); 