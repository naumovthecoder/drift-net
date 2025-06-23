using DriftControlDeck.Models;
using Microsoft.Data.Sqlite;

namespace DriftControlDeck.Services;

public class ProfileService
{
    private readonly string _dbPath;
    private readonly ILogger<ProfileService> _logger;

    public ProfileService(IConfiguration cfg, ILogger<ProfileService> logger)
    {
        _dbPath = cfg["ProfilesDbPath"] ?? "profiles.db";
        _logger = logger;
        Init();
    }

    private void Init()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS profiles (id TEXT PRIMARY KEY, name TEXT, env TEXT)";
        cmd.ExecuteNonQuery();
    }

    public IEnumerable<Profile> GetAll()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, env FROM profiles";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            yield return new Profile
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Env = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(2)) ?? new()
            };
        }
    }

    public void Create(Profile profile)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO profiles (id, name, env) VALUES ($id, $name, $env)";
        cmd.Parameters.AddWithValue("$id", profile.Id);
        cmd.Parameters.AddWithValue("$name", profile.Name);
        cmd.Parameters.AddWithValue("$env", System.Text.Json.JsonSerializer.Serialize(profile.Env));
        cmd.ExecuteNonQuery();
    }

    public void Update(string id, Profile profile)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE profiles SET name=$name, env=$env WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", profile.Name);
        cmd.Parameters.AddWithValue("$env", System.Text.Json.JsonSerializer.Serialize(profile.Env));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM profiles WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
} 