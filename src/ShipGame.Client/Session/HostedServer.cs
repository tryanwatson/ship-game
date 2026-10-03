using System;
using System.Threading;
using ShipGame.Server;

namespace ShipGame.Client.Session;

/// <summary>
/// A game server running inside the client (the --host option): this player hosts, everyone else joins with
/// --connect. It runs on its own thread at the simulation rate, independent of the client's frame rate.
/// </summary>
public sealed class HostedServer : IDisposable
{
    private readonly GameServer _server;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;

    /// <exception cref="InvalidOperationException">The port couldn't be opened (usually: already in use).</exception>
    public HostedServer(int port)
    {
        _server = new GameServer(port, message => Console.WriteLine($"[server {DateTime.Now:HH:mm:ss}] {message}"));
        Port = _server.Port;
        _thread = ServerRunner.StartInBackground(_server, _stop.Token);
        Console.WriteLine($"Hosting on UDP {Port}. Friends join with: --connect <your address>:{Port}");
    }

    public int Port { get; }

    public void Dispose()
    {
        _stop.Cancel();
        _thread.Join(TimeSpan.FromSeconds(2));
        _server.Dispose();
        _stop.Dispose();
    }
}
