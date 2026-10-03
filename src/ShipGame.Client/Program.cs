using System.Linq;
using ShipGame.Net;

// Single-player:  dotnet run --project src/ShipGame.Client
// Host a game:    dotnet run --project src/ShipGame.Client -- --host [7777] [--no-friendly-fire]
// Join a game:    dotnet run --project src/ShipGame.Client -- --connect 127.0.0.1[:7777]
string? host = null;
var port = Protocol.DefaultPort;
var hosting = false;
var friendlyFire = !args.Contains("--no-friendly-fire");
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--host":
            hosting = true;
            host = "127.0.0.1";
            if (i + 1 < args.Length && int.TryParse(args[i + 1], out var hostPort))
                port = hostPort;
            break;
        case "--connect" when i + 1 < args.Length:
        {
            var address = args[i + 1];
            var colon = address.LastIndexOf(':');
            if (colon > 0 && int.TryParse(address[(colon + 1)..], out var parsedPort))
            {
                host = address[..colon];
                port = parsedPort;
            }
            else
            {
                host = address;
            }
            break;
        }
    }
}

using var game = new ShipGame.Client.GameClient(host, port, hosting, friendlyFire);
game.Run();
