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

    private ClientConnection Connect(NetworkConditions? conditions = null)
    {
        var client = new ClientConnection("127.0.0.1", _server.Port, conditions);
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
    public void FriendlyFire_IsAnnouncedInTheLobby_AndAppliedToTheRun()
    {
        var (a, _) = StartTwoPlayerRun();

        Assert.True(a.Lobby!.FriendlyFire);          // the server's default
        Assert.True(_server.World!.FriendlyFire);
        Assert.True(a.Replica.World.FriendlyFire);   // so the client can light up other players in its lanes
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
    public void MortarShells_AreVisibleToEveryone_WhileInTheAir()
    {
        var (a, b) = StartTwoPlayerRun();
        var aim = _server.World!.GetPlayerShip(a.LocalPlayerId)!.Position + new Vector2(20, 0);

        a.Send(new CastAbilityCommand(0, AbilitySlot.Three, aim));
        PumpUntil(() => b.Replica.World.Strikes.Count == 1, "b to see a's shell in the air");

        var shell = b.Replica.World.Strikes[0];
        Assert.Equal(aim, shell.Target);
        Assert.Equal(Mortar.BlastRadius, shell.Radius);
        PumpUntil(() => b.Replica.World.Strikes.Count == 0, "the shell to land");
        Assert.Contains(b.TakeEvents(), e => e is AreaStrikeImpact impact && impact.Target == aim);
    }

    [Fact]
    public void LongGunShots_KeepTheirSizeOnOtherClients()
    {
        var (a, b) = StartTwoPlayerRun();
        var ship = _server.World!.GetPlayerShip(a.LocalPlayerId)!;

        a.Send(new CastAbilityCommand(0, AbilitySlot.Two, ship.Position + new Vector2(0, -10)));
        PumpUntil(() => b.Replica.World.Projectiles.Count == 1, "b to see the long gun shot");

        Assert.Equal(LongGun.ShotRadius, b.Replica.World.Projectiles[0].Radius);
    }

    [Fact]
    public void BroadsideDecks_ReloadSeparately_OnTheClientToo()
    {
        var (a, _) = StartTwoPlayerRun();
        var ship = _server.World!.GetPlayerShip(a.LocalPlayerId)!;

        a.Send(new CastAbilityCommand(0, AbilitySlot.One, ship.Position + new Vector2(0, 5))); // starboard
        var mirrored = a.Replica.World.FindShip(ship.Id)!;
        PumpUntil(() => !mirrored.GetAbility(AbilitySlot.One)!.IsChannelReady(BroadsideVolley.StarboardChannel),
            "a's client to show the starboard deck reloading");

        Assert.True(mirrored.GetAbility(AbilitySlot.One)!.IsChannelReady(BroadsideVolley.PortChannel));
    }

    [Fact]
    public void SimulatedLag_DelaysBothDirections()
    {
        var lagged = Connect(new NetworkConditions(LagMs: 400));
        var started = _clock.Elapsed.TotalSeconds;
        PumpUntil(() => lagged.Status == ConnectionStatus.Lobby, "the lobby");
        Assert.True(_clock.Elapsed.TotalSeconds - started >= 0.2, "the welcome should be held for half the lag");

        // Readying up goes out after 200 ms, the run starts on the server, and the news takes another 200 ms back.
        started = _clock.Elapsed.TotalSeconds;
        lagged.SetReady();
        PumpUntil(() => _server.World is not null, "the server to start the run");
        Assert.True(_clock.Elapsed.TotalSeconds - started >= 0.2, "the ready should be held for half the lag");
        PumpUntil(() => lagged.Status == ConnectionStatus.InRun, "the client to hear the run started");
        Assert.True(_clock.Elapsed.TotalSeconds - started >= 0.4, "a full round trip should have passed");
        Assert.True(lagged.RoundTripMs >= 400);
    }

    [Fact]
    public void OwnShip_IsPredicted_AheadOfTheServer_OnALaggyConnection()
    {
        var client = Connect(new NetworkConditions(LagMs: 300));
        PumpUntil(() => client.Status == ConnectionStatus.Lobby, "the lobby");
        client.SetReady();
        PumpUntil(() => client.Replica.World.GetPlayerShip(client.LocalPlayerId) is not null, "our ship");
        client.Send(new AdjustThrottleCommand(0, 3));
        PumpFor(2.0);

        // The helm answers at once on the client, while the order is still on its way to the server.
        client.Send(new SetRudderCommand(0, 1));
        PumpFor(0.05);
        var serverShip = _server.World!.GetPlayerShip(client.LocalPlayerId)!;
        Assert.Equal(1, client.Replica.World.GetPlayerShip(client.LocalPlayerId)!.Rudder);
        Assert.Equal(0, serverShip.Rudder);
        PumpUntil(() => client.Replica.Predictor.PendingCount == 0, "the server to confirm the order");

        // Steady state: where the client draws its ship now is where the server's ship will be at that tick.
        PumpFor(1.0);
        var predictTick = (long)Math.Round(client.Replica.PredictTick);
        var predicted = client.Replica.World.GetPlayerShip(client.LocalPlayerId)!.Position;
        Assert.True(predictTick > _server.World.Tick, "prediction should run ahead of the server");
        PumpUntil(() => _server.World.Tick >= predictTick, "the server to catch up");
        var distance = Vector2.Distance(predicted, serverShip.Position);
        Assert.True(distance < 0.5f, $"predicted {predicted}, server got to {serverShip.Position} ({distance} tiles off)");
    }

    [Fact]
    public void OwnShots_LeaveTheDrawnHull_ThenJoinTheirTruePath()
    {
        var client = Connect(new NetworkConditions(LagMs: 300));
        PumpUntil(() => client.Status == ConnectionStatus.Lobby, "the lobby");
        client.SetReady();
        PumpUntil(() => client.Replica.World.GetPlayerShip(client.LocalPlayerId) is not null, "our ship");
        client.Send(new AdjustThrottleCommand(0, 5));
        PumpFor(3.0); // up to full speed, so the predicted ship is well ahead of the server's timeline
        client.TakeEvents();

        var serverShip = _server.World!.GetPlayerShip(client.LocalPlayerId)!;
        var abeam = serverShip.Position + new Vector2(-serverShip.Forward.Y, serverShip.Forward.X) * 6f;
        client.Send(new CastAbilityCommand(0, AbilitySlot.One, abeam));

        // The frame the volley appears, each ball should sit where it was on the server's hull, but on the hull as
        // drawn, rather than back where the server's ship was when it fired.
        var serverTrack = new Dictionary<long, Vector2>();
        ProjectileSpawned[] volley = Array.Empty<ProjectileSpawned>();
        PumpUntil(() =>
        {
            serverTrack[_server.World.Tick] = serverShip.Position;
            return (volley = client.TakeEvents().OfType<ProjectileSpawned>().ToArray()).Length > 0;
        }, "the volley");
        var drawnShip = client.Replica.World.GetPlayerShip(client.LocalPlayerId)!.Position;
        foreach (var shot in volley)
        {
            var firedFrom = serverTrack[shot.Tick];
            Assert.True(Vector2.Distance(firedFrom, drawnShip) > 1.5f,
                $"the drawn ship should be well ahead of where it fired from (else this test proves nothing), was {Vector2.Distance(firedFrom, drawnShip)}");
            var drawnShot = client.Replica.World.Projectiles.Single(p => p.Id == shot.ProjectileId).Position;
            var misplaced = Vector2.Distance(drawnShot - drawnShip, shot.Position - firedFrom);
            Assert.True(misplaced < 0.3f, $"shot drawn {misplaced} tiles off its place on the hull");
        }

        // Once blended in, they fly exactly where the server says.
        PumpFor((ClientReplica.ShotConvergeTicks + 2) / SimConstants.TickRate);
        foreach (var shot in volley)
        {
            if (client.Replica.World.Projectiles.FirstOrDefault(p => p.Id == shot.ProjectileId) is not { } projectile)
                continue;
            var flown = (float)(client.Replica.RenderTick - shot.Tick) * SimConstants.TickDelta;
            Assert.True(Vector2.Distance(projectile.Position, shot.Position + shot.Velocity * flown) < 0.01f);
        }
    }

    [Fact]
    public void SimulatedLoss_DropsSomeSnapshots_ButNeverReliableMessages()
    {
        var lossy = Connect(new NetworkConditions(LossPercent: 50));
        PumpUntil(() => lossy.Status == ConnectionStatus.Lobby, "the lobby");
        lossy.SetReady();
        PumpUntil(() => lossy.Replica.World.GetPlayerShip(lossy.LocalPlayerId) is not null, "our ship");

        var startTick = lossy.Replica.LatestSnapshotTick;
        var snapshots = 0;
        var lastSeen = startTick;
        PumpUntil(() =>
        {
            if (lossy.Replica.LatestSnapshotTick != lastSeen)
            {
                snapshots++;
                lastSeen = lossy.Replica.LatestSnapshotTick;
            }
            return _server.World!.Tick >= startTick + 60;
        }, "two seconds of play");

        // 30 snapshots were sent; about half should have arrived (each single-chunk with one ship).
        Assert.InRange(snapshots, 5, 25);
    }

    [Fact]
    public void Discovery_ReachesEveryClient_AsOneTeamMap()
    {
        var (a, b) = StartTwoPlayerRun();
        a.Send(new AdjustThrottleCommand(0, 5)); // only a sails; b's map should fill in too
        PumpFor(3.0);

        var server = _server.World!.Discovery;
        Assert.True(server.DiscoveredCount(Team.Players) > 0);
        // Clients draw a little behind the server, so allow for the last few ticks' discoveries still in flight.
        foreach (var client in new[] { a, b })
            Assert.InRange(client.Replica.World.Discovery.DiscoveredCount(Team.Players),
                server.DiscoveredCount(Team.Players) - 40, server.DiscoveredCount(Team.Players));
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

    [Fact]
    public void Trade_IsMirrored_FromTheBoardToTheHoldToTheSea()
    {
        var (a, b) = StartTwoPlayerRun();
        var world = _server.World!;
        var post = world.Islands.First(i => i.HasShipyard);
        PumpUntil(() => a.Replica.World.Trade.OffersAt(post.Id).SequenceEqual(world.Trade.OffersAt(post.Id)), "offers on the client");

        // Moor a's ship just off the trading post and buy its first contract.
        var ship = world.GetPlayerShip(a.LocalPlayerId)!;
        var outward = Vector2.Normalize(new Vector2(-1, -1));
        var spot = post.Center;
        while (post.DistanceTo(spot) < 2f)
            spot += outward * 0.25f;
        ship.Position = ship.PreviousPosition = spot;
        ship.IsAnchored = true;
        world.AddGold(a.LocalPlayerId, 100);
        var offer = world.Trade.OffersAt(post.Id)[0];
        a.Send(new PurchaseContractCommand(0, offer.Id));

        PumpUntil(() => new[] { a, b }.All(c => c.Replica.World.FindShip(ship.Id)?.Cargo.Count == 1), "cargo aboard on both clients");
        Assert.Equal(offer, a.Replica.World.FindShip(ship.Id)!.Cargo[0].Contract);
        PumpUntil(() => a.Replica.World.Trade.OffersAt(post.Id).SequenceEqual(world.Trade.OffersAt(post.Id)), "the restocked board");

        ship.Health = 0;
        PumpUntil(() => new[] { a, b }.All(c => c.Replica.World.Trade.Crates.Count == 1), "the spilled crate on both clients");
        Assert.Equal(world.Trade.Crates.Single(), b.Replica.World.Trade.Crates.Single());
    }
}
