using System.Diagnostics;
using System.Numerics;
using ShipGame.Net;
using ShipGame.Server;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Simulation;

namespace ShipGame.Net.Tests;

/// <summary>
/// Real UDP over localhost: an in-process server and clients, pumped by hand. Each test waits on conditions
/// (with a timeout) rather than fixed delays, since packets arrive asynchronously.
/// </summary>
public sealed class LoopbackTests : IDisposable
{
    private readonly GameServer _server = new(port: 0) { NextRunSeed = 1 };
    private readonly List<ClientConnection> _clients = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastPump;

    public void Dispose()
    {
        foreach (var client in _clients)
            client.Dispose();
        _server.Dispose();
    }

    private ClientConnection Connect()
    {
        var client = new ClientConnection("127.0.0.1", _server.Port);
        _clients.Add(client);
        return client;
    }

    /// <summary>Runs the server at its tick rate and the clients every millisecond until the condition holds.</summary>
    private void PumpUntil(Func<bool> condition, string what, double timeoutSeconds = 10)
    {
        var deadline = _clock.Elapsed.TotalSeconds + timeoutSeconds;
        var nextTick = _clock.Elapsed.TotalSeconds;
        while (!condition())
        {
            if (_clock.Elapsed.TotalSeconds > deadline)
                throw new TimeoutException($"Timed out waiting for: {what}");
            Pump(ref nextTick);
        }
    }

    private void PumpFor(double seconds)
    {
        var until = _clock.Elapsed.TotalSeconds + seconds;
        PumpUntil(() => _clock.Elapsed.TotalSeconds >= until, $"{seconds}s to pass", seconds + 5);
    }

    private void Pump(ref double nextTick)
    {
        var now = _clock.Elapsed.TotalSeconds;
        if (now >= nextTick)
        {
            _server.Tick();
            nextTick += SimConstants.TickDelta;
        }
        else
        {
            _server.Poll();
        }

        var elapsed = now - _lastPump;
        _lastPump = now;
        foreach (var client in _clients)
            client.Update(elapsed);
        Thread.Sleep(1);
    }

    private (ClientConnection A, ClientConnection B) StartTwoPlayerRun()
    {
        var a = Connect();
        var b = Connect();
        PumpUntil(() => a.Status == ConnectionStatus.Lobby && b.Status == ConnectionStatus.Lobby, "both in the lobby");
        a.SetReady();
        b.SetReady();
        PumpUntil(() => a.Status == ConnectionStatus.InRun && b.Status == ConnectionStatus.InRun, "run to start");
        PumpUntil(() => a.Replica.World.Ships.Count(s => s.OwnerPlayerId is not null) == 2
                        && b.Replica.World.Ships.Count(s => s.OwnerPlayerId is not null) == 2, "both ships on both clients");
        return (a, b);
    }

    [Fact]
    public void Clients_JoinTheLobby_WithDistinctPlayerIds()
    {
        var a = Connect();
        var b = Connect();

        PumpUntil(() => a.Lobby?.Players.Count == 2 && b.Lobby?.Players.Count == 2, "lobby of two");

        Assert.NotEqual(0, a.LocalPlayerId);
        Assert.NotEqual(a.LocalPlayerId, b.LocalPlayerId);
        Assert.Null(_server.World);
    }

    [Fact]
    public void Run_StartsOnlyWhenEveryoneIsReady()
    {
        var a = Connect();
        var b = Connect();
        PumpUntil(() => a.Status == ConnectionStatus.Lobby && b.Status == ConnectionStatus.Lobby, "both in the lobby");

        a.SetReady();
        PumpFor(0.3);
        Assert.Null(_server.World);

        b.SetReady();
        PumpUntil(() => _server.World is not null, "run to start");
        Assert.Equal(2, _server.World!.Ships.Count(s => s.OwnerPlayerId is not null));
    }

    [Fact]
    public void Replicas_TrackTheServersShips()
    {
        var (a, b) = StartTwoPlayerRun();
        a.Send(new AdjustThrottleCommand(0, 3)); // player id is filled in by the server
        PumpFor(2.0);

        var serverShip = _server.World!.GetPlayerShip(a.LocalPlayerId)!;
        Assert.True(serverShip.Speed > 1f, "the command should have got the server's ship moving");
        foreach (var client in new[] { a, b })
        {
            var mirrored = client.Replica.World.FindShip(serverShip.Id)!;
            var drawn = Vector2.Lerp(mirrored.PreviousPosition, mirrored.Position, client.Replica.Alpha);
            // Drawn ~100 ms behind the server, so within about one tenth of a second of travel.
            Assert.True(Vector2.Distance(drawn, serverShip.Position) < serverShip.Speed * 0.3f + 0.3f,
                $"replica at {drawn}, server at {serverShip.Position}");
            Assert.Equal(serverShip.Throttle, mirrored.Throttle);
        }
    }

    [Fact]
    public void Commands_OnlyEverMoveTheSendersOwnShip()
    {
        var (a, b) = StartTwoPlayerRun();

        b.Send(new AdjustThrottleCommand(a.LocalPlayerId, 5)); // b pretends to be a
        PumpFor(1.0);

        Assert.Equal(0, _server.World!.GetPlayerShip(a.LocalPlayerId)!.Throttle);
        Assert.Equal(5, _server.World.GetPlayerShip(b.LocalPlayerId)!.Throttle);
    }

    [Fact]
    public void Volleys_ReachOtherClients_AndFlyThere()
    {
        var (a, b) = StartTwoPlayerRun();

        a.Send(new CastAbilityCommand(0, AbilitySlot.One, Vector2.Zero));
        PumpUntil(() => b.Replica.World.Projectiles.Count == BroadsideVolley.CannonCount, "b to see a's volley");

        var shooter = b.Replica.World.GetPlayerShip(a.LocalPlayerId)!;
        Assert.All(b.Replica.World.Projectiles, p => Assert.Equal(shooter.Id, p.OwnerShipId));
        var start = b.Replica.World.Projectiles.Select(p => p.Position).ToList();
        PumpFor(0.2);
        Assert.NotEqual(start, b.Replica.World.Projectiles.Select(p => p.Position).ToList());

        PumpUntil(() => b.Replica.World.Projectiles.Count == 0, "the balls to run out of range");
        Assert.Contains(b.TakeEvents(), e => e is AbilityCast cast && cast.ShipId == shooter.Id);
    }

    [Fact]
    public void Rejections_GoOnlyToTheSender()
    {
        var (a, b) = StartTwoPlayerRun();

        a.Send(new PurchaseUpgradeCommand(0, "speed")); // not at a shipyard
        PumpFor(0.5);

        Assert.Contains(a.TakeEvents(), e => e is CommandRejected { Reason: RejectionReason.NotAtShipyard });
        Assert.DoesNotContain(b.TakeEvents(), e => e is CommandRejected);
    }

    [Fact]
    public void JoiningMidRun_IsRefusedWithAReason()
    {
        StartTwoPlayerRun();

        var late = Connect();
        PumpUntil(() => late.Status == ConnectionStatus.Disconnected, "the late joiner to be turned away");

        Assert.Equal("RUN IN PROGRESS - TRY AGAIN SOON", late.DisconnectReason);
    }

    [Fact]
    public void LeavingMidRun_RemovesTheShipForEveryoneElse()
    {
        var (a, b) = StartTwoPlayerRun();
        var leavingShipId = _server.World!.GetPlayerShip(a.LocalPlayerId)!.Id;

        a.Dispose();
        _clients.Remove(a);
        PumpUntil(() => b.Replica.World.FindShip(leavingShipId) is null, "a's ship to vanish on b", timeoutSeconds: 15);

        Assert.NotNull(_server.World);
        Assert.Equal(1, _server.PlayerCount);
    }

    [Fact]
    public void Wipe_SendsEveryoneBackToTheLobby()
    {
        var (a, b) = StartTwoPlayerRun();

        foreach (var ship in _server.World!.Ships.Where(s => s.OwnerPlayerId is not null))
            ship.Health = 0f;
        PumpUntil(() => a.Status == ConnectionStatus.Lobby && b.Status == ConnectionStatus.Lobby, "back to the lobby");

        Assert.Null(_server.World);
        PumpUntil(() => a.Replica.World.IsRunOver, "a's replica to show the run as over");
        Assert.False(a.Lobby!.RunInProgress);
    }

    [Fact]
    public void Waves_AppearOnClients()
    {
        var (a, _) = StartTwoPlayerRun();

        PumpUntil(() => a.Replica.World.Ships.Any(s => s.Team == Team.Pirates), "the first wave to show up", timeoutSeconds: 15);

        Assert.Equal(1, a.Replica.World.Waves!.Wave);
        Assert.All(a.Replica.World.Ships.Where(s => s.Team == Team.Pirates), p => Assert.Equal(NpcStance.Guarding, p.Stance));
    }
}
