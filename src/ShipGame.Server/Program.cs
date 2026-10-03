using ShipGame.Net;
using ShipGame.Server;

// Dedicated server: dotnet run --project src/ShipGame.Server -- [--port 7777]
var port = Protocol.DefaultPort;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--port" && int.TryParse(args[i + 1], out var parsed))
        port = parsed;
}

using var server = new GameServer(port, message => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}"));
Console.WriteLine($"ShipGame server listening on UDP {server.Port} (protocol v{Protocol.Version}). Ctrl+C to stop.");

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

ServerRunner.Run(server, stop.Token);
Console.WriteLine("Server stopped.");
