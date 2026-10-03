using System.Diagnostics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Server;

/// <summary>
/// Drives a <see cref="GameServer"/> in real time: ticks at the simulation rate, polls the network in between,
/// and never fast-forwards after a stall. Used by the dedicated server and by clients hosting a game.
/// </summary>
public static class ServerRunner
{
    /// <summary>Runs on the calling thread until <paramref name="stop"/> is cancelled.</summary>
    public static void Run(GameServer server, CancellationToken stop)
    {
        var clock = Stopwatch.StartNew();
        var tickSeconds = 1.0 / SimConstants.TickRate;
        var nextTick = clock.Elapsed.TotalSeconds;
        while (!stop.IsCancellationRequested)
        {
            var now = clock.Elapsed.TotalSeconds;
            if (now >= nextTick)
            {
                server.Tick();
                nextTick += tickSeconds;
                if (now - nextTick > 0.25)
                    nextTick = now; // fell far behind (debugger, sleep): skip ahead rather than fast-forward
            }
            else
            {
                server.Poll();
                Thread.Sleep(1);
            }
        }
    }

    /// <summary>
    /// Runs on a background thread. Only that thread touches the server from then on; stop it by cancelling
    /// <paramref name="stop"/> and joining the returned thread before disposing the server.
    /// </summary>
    public static Thread StartInBackground(GameServer server, CancellationToken stop)
    {
        var thread = new Thread(() => Run(server, stop)) { IsBackground = true, Name = "GameServer" };
        thread.Start();
        return thread;
    }
}
