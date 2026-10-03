using System.Runtime.InteropServices;
using ShipGame.Net;
using ShipGame.Server;

// Dedicated server: dotnet run --project src/ShipGame.Server -- [--port 7777] [--no-friendly-fire]
ServerOptions options;
try
{
    options = ServerOptions.Parse(args, Environment.GetEnvironmentVariable);
}
catch (FormatException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(ServerOptions.Usage);
    return 2;
}

using var server = new GameServer(options.Port, message => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}"), options.FriendlyFire);
Console.WriteLine($"ShipGame server listening on UDP {server.Port} (protocol v{Protocol.Version}), friendly fire {(options.FriendlyFire ? "on" : "off")}. Ctrl+C to stop.");

// Ctrl+C, and SIGTERM from `docker stop` or systemd: finish the current tick, then say goodbye to everyone.
using var stop = new CancellationTokenSource();
void RequestStop(PosixSignalContext context)
{
    context.Cancel = true;
    Console.WriteLine($"Received {context.Signal}, shutting down.");
    stop.Cancel();
}
using var onInterrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, RequestStop);
using var onTerminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, RequestStop);

ServerRunner.Run(server, stop.Token);
Console.WriteLine("Server stopped.");
return 0;
