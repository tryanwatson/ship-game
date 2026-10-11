using System.Diagnostics;
using System.Numerics;
using ShipGame.Net;
using ShipGame.Server;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;

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

    /// <param name="weaponA">The weapon a starts with (on slot 1); b always starts with the broadside.</param>
    private (ClientConnection A, ClientConnection B) StartTwoPlayerRun(string weaponA = BroadsideVolley.AbilityId)
    {
        var a = Connect();
        var b = Connect();
        PumpUntil(() => a.Status == ConnectionStatus.Lobby && b.Status == ConnectionStatus.Lobby, "both in the lobby");
        a.ReadyUp("ANNE");
        b.ReadyUp("MARY");
        PumpUntil(() => a.Status == ConnectionStatus.InRun && b.Status == ConnectionStatus.InRun, "run to start");
        PumpUntil(() => a.Replica.World.Ships.Count(s => s.OwnerPlayerId is not null) == 2
                        && b.Replica.World.Ships.Count(s => s.OwnerPlayerId is not null) == 2, "both ships on both clients");
        PumpUntil(() => a.SetSail(weaponA) & b.SetSail() && !_server.World!.IsPaused, "the run to get under way");
        return (a, b);
    }

    /// <summary>The first fortress the start leads to.</summary>
    private static int FirstFortress(World world) =>
        world.Director!.CurrentNode!.Next.First(id => world.Director.Chart!.Find(id)!.Kind == NodeKind.Fortress);

    /// <summary>Both vote for <paramref name="nodeId"/>, and the crew sails there, on the server and both clients.</summary>
    private void SailTo(ClientConnection a, ClientConnection b, int nodeId)
    {
        a.Send(new ChooseCourseCommand(0, nodeId));
        b.Send(new ChooseCourseCommand(0, nodeId));
        PumpUntil(() => new[] { a, b }.All(c => c.Replica.World.Director!.NodeId == nodeId
                                               && c.Replica.World.Islands.Select(i => i.Id).SequenceEqual(_server.World!.Islands.Select(i => i.Id))),
            "the crew to sail on, on both clients");
    }

    [Fact]
    public void TheVoyage_ReachesTheClients_TheChartTheRegionsAndTheVotes()
    {
        var (a, b) = StartTwoPlayerRun();
        var world = _server.World!;
        var director = world.Director!;

        // The chart, and the start's open water.
        PumpUntil(() => a.Replica.World.Director!.Chart is not null && b.Replica.World.Director!.Chart is not null, "the chart");
        Assert.Equal(director.Chart!.Nodes, a.Replica.World.Director!.Chart!.Nodes);
        Assert.Equal(world.WorldSize, a.Replica.World.WorldSize);
        Assert.Equal(world.Islands.Select(i => (i.Id, i.Name, i.Center)), a.Replica.World.Islands.Select(i => (i.Id, i.Name, i.Center)));
        PumpUntil(() => a.Replica.World.Director!.Cleared, "the call to chart a course");

        // a votes: both clients see it, and the crew waits for b.
        var start = director.NodeId;
        var course = FirstFortress(world);
        a.Send(new ChooseCourseCommand(0, course));
        PumpUntil(() => b.Replica.World.Players[a.LocalPlayerId].CourseVote == course, "a's vote on b's client");
        Assert.Equal(start, director.NodeId);

        // b votes: the crew sails into the fortress's waters, everywhere.
        var regions = a.Replica.World.RegionsEntered;
        b.Send(new ChooseCourseCommand(0, course));
        PumpUntil(() => new[] { a, b }.All(c => c.Replica.World.Director!.NodeId == course), "the crew to sail on");
        PumpUntil(() => a.Replica.World.Islands.Count == world.Islands.Count, "the new islands");
        Assert.Equal(world.WorldSize, a.Replica.World.WorldSize);
        Assert.Equal(world.Islands.Select(i => (i.Id, i.Name, i.Level, i.IsFortress)), a.Replica.World.Islands.Select(i => (i.Id, i.Name, i.Level, i.IsFortress)));
        Assert.Equal(world.Islands.Single(i => i.IsFortress).Outline.ToArray(), a.Replica.World.Islands.Single(i => i.IsFortress).Outline.ToArray());
        Assert.False(a.Replica.World.Director!.Cleared);
        Assert.Equal(regions + 1, a.Replica.World.RegionsEntered); // the client's scenery, wakes and wrecks start over
        Assert.Null(a.Replica.World.Players[a.LocalPlayerId].CourseVote);
        Assert.Equal(new[] { start, course }, a.Replica.World.Director!.Route);

        // Our own ship is drawn where it came in, not where it was.
        var ship = world.GetPlayerShip(a.LocalPlayerId)!;
        PumpUntil(() => a.Replica.World.FindShip(ship.Id) is { } drawn && Vector2.Distance(drawn.Position, ship.Position) < 2f, "a drawn at the entry");
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

        a.ReadyUp();
        PumpFor(0.3);
        Assert.Null(_server.World);

        b.ReadyUp();
        PumpUntil(() => _server.World is not null, "run to start");
        Assert.Equal(2, _server.World!.Ships.Count(s => s.OwnerPlayerId is not null));
    }

    [Fact]
    public void StartingGold_SetInTheLobby_IsSharedAndPaidAtTheStart()
    {
        var a = Connect();
        var b = Connect();
        PumpUntil(() => a.Lobby?.Players.Count == 2 && b.Lobby?.Players.Count == 2, "lobby of two");

        b.SetStartingGold(Runs.MaxStartingGold + 1); // clamped
        PumpUntil(() => a.Lobby!.StartingGold == Runs.MaxStartingGold, "a to see b's setting");
        b.SetStartingGold(500);
        PumpUntil(() => a.Lobby!.StartingGold == 500, "a to see the new setting");

        foreach (var client in new[] { a, b })
            client.ReadyUp();
        PumpUntil(() => a.Status == ConnectionStatus.InRun, "run to start");
        PumpUntil(() => a.Replica.World.Players.TryGetValue(a.LocalPlayerId, out var p) && p.Gold == 500, "a's gold to arrive");
        Assert.Equal(500, _server.World!.Players[b.LocalPlayerId].Gold);
    }

    [Fact]
    public void ReadyingUp_IsRefused_UntilNamed_AndTheRunOpensWithTheStartingChoices()
    {
        var client = Connect();
        PumpUntil(() => client.Status == ConnectionStatus.Lobby, "the lobby");

        client.SetReady();
        client.SetName("!!");
        PumpFor(0.3);
        Assert.Null(_server.World);
        Assert.False(client.Lobby!.Players.Single().Ready);

        client.SetName("calico jack");
        PumpUntil(() => client.Lobby!.Players.Single().Name == "CALICO JACK", "the name to show in the lobby");
        client.SetReady();
        PumpUntil(() => _server.World is not null, "run to start");
        PumpUntil(() => client.Replica.World.IsPaused, "the starting choices on the client");
        Assert.Equal("CALICO JACK", client.Replica.World.Players[client.LocalPlayerId].Name);
        Assert.True(client.Replica.World.Players[client.LocalPlayerId].NeedsStartingWeapon);
        PumpUntil(() => client.SetSail(Mortar.AbilityId), "the run to get under way");

        // Only the chosen weapon, on 1; the rest are bought later.
        var ship = _server.World!.GetPlayerShip(client.LocalPlayerId)!;
        Assert.IsType<Mortar>(ship.GetAbility(AbilitySlot.One)!.Definition);
        Assert.All(ship.Abilities.Skip(1), Assert.Null);
    }

    [Fact]
    public void BoughtWeaponsAndSkills_ReachTheClients()
    {
        var (a, b) = StartTwoPlayerRun();
        var ship = _server.World!.GetPlayerShip(a.LocalPlayerId)!;

        ship.SetAbility(AbilitySlot.Two, WeaponCatalog.LongGun.Ability);
        ship.AddSkill(SkillTrees.Find("heavy-volley")!);
        var mirrored = b.Replica.World.FindShip(ship.Id)!;
        PumpUntil(() => mirrored.HasAbility(LongGun.AbilityId) && mirrored.HasSkill("heavy-volley"), "b to see a's new gun and skill");

        // The skill changes the volley everyone sees.
        a.Send(new CastAbilityCommand(0, AbilitySlot.One, ship.Position + new Vector2(0, 5)));
        PumpUntil(() => b.Replica.World.Projectiles.Count == BroadsideVolley.CannonCount + 2, "b to see a's heavier volley");
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
        var (a, b) = StartTwoPlayerRun(Mortar.AbilityId);
        var aim = _server.World!.GetPlayerShip(a.LocalPlayerId)!.Position + new Vector2(15, 0);

        a.Send(new CastAbilityCommand(0, AbilitySlot.One, aim));
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
        var (a, b) = StartTwoPlayerRun(LongGun.AbilityId);
        var ship = _server.World!.GetPlayerShip(a.LocalPlayerId)!;

        a.Send(new CastAbilityCommand(0, AbilitySlot.One, ship.Position + new Vector2(0, -10)));
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
        lagged.ReadyUp();
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
        client.ReadyUp();
        PumpUntil(() => client.Replica.World.GetPlayerShip(client.LocalPlayerId) is not null, "our ship");
        PumpUntil(() => client.SetSail(), "the run to get under way");
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
    public void OwnShots_LeaveTheDrawnHull_TheMomentTheyreFired_AndKeepPaceWithIt()
    {
        var client = Connect(new NetworkConditions(LagMs: 300));
        PumpUntil(() => client.Status == ConnectionStatus.Lobby, "the lobby");
        client.ReadyUp();
        PumpUntil(() => client.Replica.World.GetPlayerShip(client.LocalPlayerId) is not null, "our ship");
        PumpUntil(() => client.SetSail(), "the run to get under way");
        client.Send(new AdjustThrottleCommand(0, 5));
        PumpFor(3.0); // up to full speed, so the predicted ship is well ahead of the server's timeline
        client.TakeEvents();

        var drawn = client.Replica.World.GetPlayerShip(client.LocalPlayerId)!;
        var abeam = drawn.Position + new Vector2(-drawn.Forward.Y, drawn.Forward.X) * 6f;
        client.Send(new CastAbilityCommand(0, AbilitySlot.One, abeam));

        // Fired here and now, from the hull as drawn: no waiting a round trip for the server.
        var volley = client.Replica.World.Projectiles.Where(p => p.OwnerShipId == drawn.Id).ToArray();
        Assert.Equal(BroadsideVolley.CannonCount, volley.Length);
        var along = volley.ToDictionary(p => p.Id, p => Vector2.Dot(p.Position - drawn.Position, drawn.Forward));
        Assert.All(volley, p => Assert.True(Vector2.Distance(p.Position, drawn.Position) < drawn.Stats.Length,
            $"shot fired {Vector2.Distance(p.Position, drawn.Position)} tiles from the drawn ship"));

        // After the server's answered, the same balls (not a second volley) are still abeam of the ship as drawn,
        // where they'd be had there been no lag at all.
        PumpFor(0.45); // a 300 ms round trip, and short of the balls' ~0.57 s flight
        var flying = client.Replica.World.Projectiles.Where(p => p.OwnerShipId == drawn.Id).ToArray();
        Assert.Equal(volley.Select(p => p.Id).Order(), flying.Select(p => p.Id).Order());
        drawn = client.Replica.World.GetPlayerShip(client.LocalPlayerId)!;
        foreach (var ball in flying)
        {
            var drift = Vector2.Dot(ball.Position - drawn.Position, drawn.Forward) - along[ball.Id];
            Assert.True(MathF.Abs(drift) < 0.4f, $"ball fell {drift} tiles fore/aft of its gun");
        }
    }

    [Fact]
    public void SimulatedLoss_DropsSomeSnapshots_ButNeverReliableMessages()
    {
        var lossy = Connect(new NetworkConditions(LossPercent: 50));
        PumpUntil(() => lossy.Status == ConnectionStatus.Lobby, "the lobby");
        lossy.ReadyUp();
        PumpUntil(() => lossy.Replica.World.GetPlayerShip(lossy.LocalPlayerId) is not null, "our ship");
        PumpUntil(() => lossy.SetSail(), "the run to get under way");

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
    public void Pirates_AppearOnClients_OnlyNearAPlayer_AndGoWhenLeftBehind()
    {
        var (a, b) = StartTwoPlayerRun();
        var world = _server.World!;
        SailTo(a, b, FirstFortress(world));

        // The fortress's garrison is at sea from the start, but nobody's near it yet: nothing is sent.
        PumpFor(0.5);
        var ship = world.GetPlayerShip(a.LocalPlayerId)!;
        var fortress = world.Islands.Single(i => i.IsFortress);
        Assert.NotEmpty(world.Ships.Where(s => s.Team == Team.Pirates));
        Assert.DoesNotContain(a.Replica.World.Ships, s => s.Team == Team.Pirates);

        // Sail a's ship (by fiat) up to it: the forts and guards come into view, with their level.
        ship.AddModifier(new ShipGame.Shared.Stats.StatModifier(ShipGame.Shared.Stats.StatId.MaxHealth, ShipGame.Shared.Stats.ModifierKind.Flat, 1e6f, "test"));
        ship.Position = ship.PreviousPosition = fortress.ShoreToward(Vector2.UnitY) + Vector2.UnitY * 25f;
        PumpUntil(() => a.Replica.World.Ships.Any(s => s.FortIslandId == fortress.Id), "the forts to show up");
        var seen = a.Replica.World.Ships.First(s => s.FortIslandId == fortress.Id);
        Assert.Equal(fortress.Level, seen.Level);
        Assert.Equal(world.FindShip(seen.Id)!.Level, seen.Level);

        // The forecast comes along too.
        PumpUntil(() => world.Director!.Status == a.Replica.World.Director!.Status, "the forecast");

        // Back to where the crew came in: the forts are hidden again (though still standing on the server).
        ship.Position = ship.PreviousPosition = new Vector2(world.WorldSize.X / 2f, world.WorldSize.Y - 2f);
        PumpUntil(() => a.Replica.World.FindShip(seen.Id) is null, "the forts to be hidden");
        Assert.NotNull(world.FindShip(seen.Id));
    }

    [Fact]
    public void TakingAFortress_ReachesTheClients_AndTheirCardChoicesReachTheServer()
    {
        var (a, b) = StartTwoPlayerRun();
        var world = _server.World!;
        SailTo(a, b, FirstFortress(world));
        var fortress = world.Islands.Single(i => i.IsFortress);

        // Raze it (by fiat), with a parked alongside so its forts are sent to the client.
        var ship = world.GetPlayerShip(a.LocalPlayerId)!;
        ship.AddModifier(new ShipGame.Shared.Stats.StatModifier(ShipGame.Shared.Stats.StatId.MaxHealth, ShipGame.Shared.Stats.ModifierKind.Flat, 1e6f, "test"));
        ship.Position = ship.PreviousPosition = fortress.ShoreToward(Vector2.UnitY) + Vector2.UnitY * 30f;
        PumpUntil(() => a.Replica.World.Ships.Any(s => s.FortIslandId == fortress.Id), "the forts to show up");
        foreach (var fort in world.Ships.Where(s => s.FortIslandId == fortress.Id))
            fort.Health = 0f;
        PumpUntil(() => !a.Replica.World.IsHeld(fortress) && !b.Replica.World.IsHeld(fortress), "the fortress taken on both clients");
        Assert.DoesNotContain(a.Replica.World.Ships, s => s.FortIslandId == fortress.Id);

        // Both clients see a's offer, the same as the server dealt it.
        var offer = world.Players[a.LocalPlayerId].CardOffers.Single();
        PumpUntil(() => a.Replica.World.Players.TryGetValue(a.LocalPlayerId, out var p) && p.CardOffers.Count == 1, "a's offer");
        Assert.Equal(offer, a.Replica.World.Players[a.LocalPlayerId].CardOffers.Single());
        Assert.Equal(1, a.Replica.World.Director!.FortressesTaken);

        // The game stops, on the server and both clients, until everyone has chosen.
        PumpUntil(() => a.Replica.World.IsPaused && b.Replica.World.IsPaused, "the pause on both clients");
        Assert.True(world.IsPaused);
        var pausedAt = world.Tick;
        var drawnAt = a.Replica.World.FindShip(ship.Id)!.Position;
        PumpFor(0.5);
        Assert.Equal(pausedAt, world.Tick);
        Assert.Equal(drawnAt, a.Replica.World.FindShip(ship.Id)!.Position);

        // a pays to reroll: the fresh hand, and the bill, reach a's client.
        world.AddGold(a.LocalPlayerId, CardRewards.RerollBaseCost);
        a.Send(new RerollCardsCommand(0));
        PumpUntil(() => a.Replica.World.Players[a.LocalPlayerId].Rerolls == 1, "the reroll on a's client");
        offer = world.Players[a.LocalPlayerId].CardOffers.Single();
        Assert.Equal(offer, a.Replica.World.Players[a.LocalPlayerId].CardOffers.Single());
        PumpUntil(() => a.Replica.World.Players[a.LocalPlayerId].Gold == world.Players[a.LocalPlayerId].Gold, "the bill on a's client");

        // a chooses: the card is played on the server, and the ship's new cards come back to both clients.
        a.Send(new ChooseCardCommand(0, offer.Cards[1].Id));
        PumpUntil(() => new[] { a, b }.All(c => c.Replica.World.FindShip(ship.Id)?.Cards.Count == 2), "the card on both clients"); // and the starting card
        Assert.Equal(offer.Cards[1], ship.Cards[^1]);
        Assert.Equal(offer.Cards[1], a.Replica.World.FindShip(ship.Id)!.Cards[^1]); // the same level and roll
        Assert.Equal(ship.Stats, a.Replica.World.FindShip(ship.Id)!.Stats);
        Assert.Empty(a.Replica.World.Players[a.LocalPlayerId].CardOffers);
        Assert.Equal(offer.Cards[1], a.Replica.World.Players[a.LocalPlayerId].Cards[^1]);
        Assert.Single(b.Replica.World.Players[b.LocalPlayerId].CardOffers); // b hasn't chosen yet...
        Assert.True(world.IsPaused && a.Replica.World.IsPaused);              // ...so everyone waits

        b.Send(new ChooseCardCommand(0, world.Players[b.LocalPlayerId].CardOffers[0].Cards[0].Id));
        PumpUntil(() => !a.Replica.World.IsPaused && !b.Replica.World.IsPaused, "play to resume on both clients");
        PumpUntil(() => world.Tick > pausedAt + 15 && a.Replica.LatestSnapshotTick > pausedAt + 15, "the clock to run again");
    }
}
