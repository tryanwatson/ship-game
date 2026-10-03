using System;
using System.Linq;
using ShipGame.Net;

// Menu (solo, host, join):  dotnet run --project src/ShipGame.Client
// Host a game:              dotnet run --project src/ShipGame.Client -- --host [7777] [--no-friendly-fire]
// Join a game:              dotnet run --project src/ShipGame.Client -- --connect 127.0.0.1[:7777] [--password P]
// Simulate a bad network:   add --lag <ms> [--jitter <ms>] [--loss <percent>] (applies to any online game)
string? host = null;
var port = Protocol.DefaultPort;
var hosting = false;
var friendlyFire = !args.Contains("--no-friendly-fire");
var conditions = new NetworkConditions();
string? password = null;
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
            if (ServerAddress.TryParse(args[i + 1], out var address))
                (host, port) = (address.Host, address.Port);
            else
                Console.Error.WriteLine($"Ignoring --connect '{args[i + 1]}': not a valid address.");
            break;
        case "--password" when i + 1 < args.Length:
            password = args[i + 1];
            break;
        case "--lag" when i + 1 < args.Length && int.TryParse(args[i + 1], out var lag):
            conditions = conditions with { LagMs = Math.Max(0, lag) };
            break;
        case "--jitter" when i + 1 < args.Length && int.TryParse(args[i + 1], out var jitter):
            conditions = conditions with { JitterMs = Math.Max(0, jitter) };
            break;
        case "--loss" when i + 1 < args.Length && int.TryParse(args[i + 1], out var loss):
            conditions = conditions with { LossPercent = Math.Clamp(loss, 0, 100) };
            break;
    }
}

if (!conditions.IsPerfect)
    Console.WriteLine($"Simulating {conditions}");

using var game = new ShipGame.Client.GameClient(host, port, hosting, friendlyFire, conditions, password);
game.Run();
