using System.Diagnostics;
using ShipGame.Net;
using ShipGame.Server;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Simulation;

namespace ShipGame.Net.Tests;

/// <summary>Passwords and rate limiting, over real UDP like <see cref="LoopbackTests"/>.</summary>
public sealed class ServerSecurityTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly List<ClientConnection> _clients = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private GameServer? _server;
    private double _lastPump;

    public void Dispose()
    {
        foreach (var client in _clients)
            client.Dispose();
        _server?.Dispose();
    }

    private GameServer StartServer(string? password = null) =>
        _server = new GameServer(port: 0, log: message => { lock (_log) _log.Add(message); }, password: password);

    private ClientConnection Connect(string? password = null)
    {
        var client = new ClientConnection("127.0.0.1", _server!.Port, password: password);
        _clients.Add(client);
        return client;
    }

    private void PumpUntil(Func<bool> condition, string what, double timeoutSeconds = 10)
    {
        var deadline = _clock.Elapsed.TotalSeconds + timeoutSeconds;
        var nextTick = _clock.Elapsed.TotalSeconds;
        while (!condition())
        {
            if (_clock.Elapsed.TotalSeconds > deadline)
                throw new TimeoutException($"Timed out waiting for: {what}");
            var now = _clock.Elapsed.TotalSeconds;
            if (now >= nextTick)
            {
                _server!.Tick();
                nextTick += SimConstants.TickDelta;
            }
            else
            {
                _server!.Poll();
            }
            foreach (var client in _clients)
                client.Update(now - _lastPump);
            _lastPump = now;
            Thread.Sleep(1);
        }
    }

    private void PumpFor(double seconds, Action? eachPump = null)
    {
        var until = _clock.Elapsed.TotalSeconds + seconds;
        PumpUntil(() =>
        {
            eachPump?.Invoke();
            return _clock.Elapsed.TotalSeconds >= until;
        }, $"{seconds}s to pass", seconds + 5);
    }

    private bool Logged(string fragment)
    {
        lock (_log)
            return _log.Any(line => line.Contains(fragment));
    }

    [Fact]
    public void PasswordProtectedServer_AdmitsTheRightPassword_AndRefusesOthers()
    {
        StartServer(password: "anchors aweigh");
        var right = Connect("anchors aweigh");
        var wrong = Connect("anchors away");
        var none = Connect();

        PumpUntil(() => right.Status == ConnectionStatus.Lobby
                        && wrong.Status == ConnectionStatus.Disconnected
                        && none.Status == ConnectionStatus.Disconnected, "everyone to be let in or turned away");

        Assert.Equal("WRONG PASSWORD", wrong.DisconnectReason);
        Assert.Equal("WRONG PASSWORD", none.DisconnectReason);
        Assert.Equal(1, _server!.PlayerCount);
    }

    [Fact]
    public void OpenServer_IgnoresAnyPasswordSent()
    {
        StartServer();
        var client = Connect("whatever");
        PumpUntil(() => client.Status == ConnectionStatus.Lobby, "the lobby");
    }

    [Fact]
    public void Flooding_IsDropped_AndTheFlooderStaysConnected()
    {
        StartServer();
        var flooder = Connect();
        PumpUntil(() => flooder.Status == ConnectionStatus.Lobby, "the lobby");
        flooder.ReadyUp();
        PumpUntil(() => flooder.Status == ConnectionStatus.InRun, "the run");
        PumpUntil(() => flooder.SetSail(), "the run to get under way");

        for (var i = 0; i < 1000; i++)
            flooder.Send(new SetRudderCommand(0, i % 2 == 0 ? 1 : -1));
        PumpFor(0.5);

        Assert.True(Logged("sending too fast"), "a flood should be dropped");
        Assert.Equal(ConnectionStatus.InRun, flooder.Status);

        // Once the budget refills, its commands count again.
        PumpFor(MessagesToSeconds(GameServer.MessageBurst));
        flooder.Send(new SetRudderCommand(0, 0));
        flooder.Send(new MoveCommand(0, _server!.World!.WorldSize / 3f));
        PumpUntil(() => _server.World!.GetPlayerShip(flooder.LocalPlayerId)!.MoveTarget is not null, "the move order to land");
    }

    [Fact]
    public void ADraggedMoveOrderEveryTick_IsNeverDropped()
    {
        StartServer();
        var client = Connect();
        PumpUntil(() => client.Status == ConnectionStatus.Lobby, "the lobby");
        client.ReadyUp();
        PumpUntil(() => client.Status == ConnectionStatus.InRun, "the run");
        PumpUntil(() => client.SetSail(), "the run to get under way");

        // What the client sends while right-drag steering: one order per tick, plus the odd key press.
        var lastSent = 0.0;
        var sent = 0;
        PumpFor(3.0, () =>
        {
            var now = _clock.Elapsed.TotalSeconds;
            if (now - lastSent < SimConstants.TickDelta)
                return;
            lastSent = now;
            client.Send(new MoveCommand(0, new System.Numerics.Vector2(50 + sent % 10, 50)));
            if (++sent % 10 == 0)
                client.Send(new AdjustThrottleCommand(0, 1));
        });

        Assert.True(sent > 60);
        Assert.False(Logged("sending too fast"));
    }

    private static double MessagesToSeconds(float messages) => messages / GameServer.MessagesPerSecond + 0.2;
}
