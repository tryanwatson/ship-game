using System;
using System.IO;
using System.Text.Json;

namespace ShipGame.Client;

/// <summary>
/// What the client remembers between launches, in <c>ShipGame/client.json</c> under the user's application data
/// folder. Missing or unreadable settings just mean defaults; failing to save is never fatal.
/// </summary>
public sealed class ClientSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The server last joined from the menu, as typed.</summary>
    public string LastAddress { get; set; } = "";

    /// <summary>The password given for <see cref="LastAddress"/> (a game server's shared password, kept in plain text).</summary>
    public string LastPassword { get; set; } = "";

    /// <summary>Whether to host with friendly fire on.</summary>
    public bool HostFriendlyFire { get; set; } = true;

    /// <summary>Gold to start solo runs with (a playtesting option; 0 normally).</summary>
    public int SoloStartingGold { get; set; }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShipGame", "client.json");

    public static ClientSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(FilePath)) ?? new ClientSettings()
                : new ClientSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.WriteLine($"Couldn't read settings ({ex.Message}); using defaults.");
            return new ClientSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Couldn't save settings: {ex.Message}");
        }
    }
}
