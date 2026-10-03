using System.Diagnostics;
using ShipGame.Net;
using ShipGame.Server;

namespace ShipGame.Net.Tests;

public class ServerRunnerTests
{
    [Fact]
    public void BackgroundServer_AcceptsPlayers_RunsAGame_AndStopsCleanly()
    {
        // As the client's --host option uses it: the server on its own thread, the client on this one.
        using var server = new GameServer(port: 0);
        using var stop = new CancellationTokenSource();
        var thread = ServerRunner.StartInBackground(server, stop.Token);
        using var client = new ClientConnection("127.0.0.1", server.Port);

        var clock = Stopwatch.StartNew();
        var last = 0.0;
        void PumpUntil(Func<bool> condition, string what)
        {
            while (!condition())
            {
                Assert.True(clock.Elapsed.TotalSeconds < 10, $"timed out waiting for {what}");
                var now = clock.Elapsed.TotalSeconds;
                client.Update(now - last);
                last = now;
                Thread.Sleep(1);
            }
        }

        PumpUntil(() => client.Status == ConnectionStatus.Lobby, "the lobby");
        client.ReadyUp();
        PumpUntil(() => client.Replica.World.GetPlayerShip(client.LocalPlayerId) is not null, "our ship to appear");
        var startTick = client.Replica.LatestSnapshotTick;
        PumpUntil(() => client.Replica.LatestSnapshotTick >= startTick + 30, "a second of play");

        stop.Cancel();
        Assert.True(thread.Join(TimeSpan.FromSeconds(2)), "server thread should stop when cancelled");
    }
}
