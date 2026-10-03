using ShipGame.Net;

namespace ShipGame.Server;

/// <summary>
/// Dedicated server settings, from environment variables (handy in containers) overridden by command-line arguments.
/// <list type="bullet">
/// <item><c>--port N</c> / <c>SHIPGAME_PORT</c>: UDP port (default <see cref="Protocol.DefaultPort"/>).</item>
/// <item><c>--no-friendly-fire</c> / <c>SHIPGAME_FRIENDLY_FIRE=false</c>: players' shots don't hurt each other.</item>
/// <item><c>--password P</c> / <c>SHIPGAME_PASSWORD</c>: players must give it to join. Empty means none.</item>
/// </list>
/// </summary>
public sealed record ServerOptions(int Port, bool FriendlyFire, string? Password = null)
{
    public const string Usage =
        "Usage: ShipGame.Server [--port N] [--friendly-fire | --no-friendly-fire] [--password P]\n" +
        "Environment: SHIPGAME_PORT, SHIPGAME_FRIENDLY_FIRE (true/false), SHIPGAME_PASSWORD. Arguments win.";

    public static ServerOptions Default { get; } = new(Protocol.DefaultPort, FriendlyFire: true);

    /// <summary>Reads settings; throws <see cref="FormatException"/> with a readable message on bad input.</summary>
    public static ServerOptions Parse(IReadOnlyList<string> args, Func<string, string?> getEnvironment)
    {
        var options = Default;

        if (getEnvironment("SHIPGAME_PORT") is { Length: > 0 } envPort)
            options = options with { Port = ParsePort(envPort, "SHIPGAME_PORT") };
        if (getEnvironment("SHIPGAME_FRIENDLY_FIRE") is { Length: > 0 } envFriendlyFire)
            options = options with { FriendlyFire = ParseBool(envFriendlyFire, "SHIPGAME_FRIENDLY_FIRE") };
        if (getEnvironment("SHIPGAME_PASSWORD") is { Length: > 0 } envPassword)
            options = options with { Password = envPassword };

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--port":
                    if (i + 1 >= args.Count)
                        throw new FormatException("--port needs a value.");
                    options = options with { Port = ParsePort(args[++i], "--port") };
                    break;
                case "--friendly-fire":
                    options = options with { FriendlyFire = true };
                    break;
                case "--no-friendly-fire":
                    options = options with { FriendlyFire = false };
                    break;
                case "--password":
                    if (i + 1 >= args.Count)
                        throw new FormatException("--password needs a value.");
                    var password = args[++i];
                    options = options with { Password = password.Length > 0 ? password : null };
                    break;
                default:
                    throw new FormatException($"Unknown argument '{args[i]}'.");
            }
        }
        return options;
    }

    private static int ParsePort(string value, string source) =>
        int.TryParse(value, out var port) && port is >= 0 and <= 65535
            ? port
            : throw new FormatException($"{source}: '{value}' isn't a port number (0-65535).");

    private static bool ParseBool(string value, string source) =>
        value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => throw new FormatException($"{source}: '{value}' should be true or false."),
        };
}
