using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

/// <summary>
/// Phase 0 of multiplayer: the world announces everything a client can't infer from snapshots, explains every
/// rejected command, and exposes NPC stance as plain state.
/// </summary>
public class WorldEventTests
{
    private const int PlayerId = 1;

    private static (World world, Ship ship) CreateWorld()
    {
        var world = new World(new Vector2(128, 128)) { Wind = Vector2.Zero };
        var ship = world.SpawnShip(new Vector2(30, 30), 0f, ShipStats.Sloop, PlayerId, Loadouts.Sloop);
        world.DrainEvents(); // discard setup
        return (world, ship);
    }

    private static List<WorldEvent> StepAndDrain(World world, int ticks = 1)
    {
        var events = new List<WorldEvent>();
        for (var i = 0; i < ticks; i++)
        {
            world.Step();
            events.AddRange(world.DrainEvents());
        }
        return events;
    }

    [Fact]
    public void DrainEvents_ReturnsEachEventOnce()
    {
        var world = new World(new Vector2(64, 64));
        world.SpawnShip(new Vector2(10, 10), 0f, ShipStats.Sloop);

        Assert.Single(world.DrainEvents().OfType<ShipSpawned>());
        Assert.Empty(world.DrainEvents());
    }

    [Fact]
    public void Casting_AnnouncesTheCastAndEveryProjectile_WithEnoughToFlyItClientSide()
    {
        var (world, ship) = CreateWorld();

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.Two, Vector2.Zero));
        var events = StepAndDrain(world);

        var cast = Assert.Single(events.OfType<AbilityCast>());
        Assert.Equal(ship.Id, cast.ShipId);
        Assert.Equal(ship.GetAbility(AbilitySlot.Two)!.CooldownDurationTicks, cast.CooldownTicks);

        var spawned = events.OfType<ProjectileSpawned>().ToList();
        Assert.Equal(BroadsideVolley.CannonCount, spawned.Count);
        foreach (var e in spawned)
        {
            // Replaying the event's straight-line flight lands exactly where the server's ball is.
            var projectile = world.Projectiles.Single(p => p.Id == e.ProjectileId);
            var ticksFlown = e.LifetimeTicks - projectile.RemainingTicks;
            Assert.Equal(e.Position + e.Velocity * SimConstants.TickDelta * ticksFlown, projectile.Position);
            Assert.Equal(Team.Players, e.Team);
        }
    }

    [Fact]
    public void Hits_AreAnnounced_WithTheShipStruck()
    {
        var (world, ship) = CreateWorld();
        var target = world.SpawnShip(new Vector2(30, 34), 0f, ShipStats.Sloop);

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.Two, Vector2.Zero));
        var events = StepAndDrain(world, SimConstants.TickRate);

        Assert.Contains(events.OfType<ProjectileImpact>(), e => e.ShipId == target.Id);
    }

    [Fact]
    public void Kills_AnnounceTheSinkingAndTheGold()
    {
        var (world, ship) = CreateWorld();
        var target = world.SpawnShip(new Vector2(30, 34), 0f, ShipStats.Sloop);
        target.Health = 1f;

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.Two, Vector2.Zero));
        var events = StepAndDrain(world, SimConstants.TickRate);

        var sunk = Assert.Single(events.OfType<ShipSunk>());
        Assert.Equal(target.Id, sunk.ShipId);
        Assert.Equal(ship.Id, sunk.KillerShipId);
        var gold = Assert.Single(events.OfType<GoldChanged>());
        Assert.Equal(KillRewards.Gold, gold.Delta);
        Assert.Equal(world.Players[PlayerId].Gold, gold.Gold);
    }

    [Fact]
    public void Plundering_AnnouncesTheIslandAndTheGold()
    {
        var world = new World(new Vector2(128, 128)) { Wind = Vector2.Zero };
        world.AddIsland(new Island(7, new[] { new Vector2(40, 26), new Vector2(48, 26), new Vector2(48, 34), new Vector2(40, 34) }));
        world.SpawnShip(new Vector2(37, 30), 0f, ShipStats.Sloop, PlayerId);
        world.Enqueue(new ToggleAnchorCommand(PlayerId));

        var events = StepAndDrain(world, Plundering.DurationTicks + 1);

        var plundered = Assert.Single(events.OfType<IslandPlundered>());
        Assert.Equal(7, plundered.IslandId);
        Assert.Equal(Plundering.CooldownTicks, plundered.CooldownTicks);
        Assert.Contains(events.OfType<GoldChanged>(), e => e.Delta == Island.DefaultPlunderGold);
    }

    [Fact]
    public void Purchases_AnnounceTheUpgrade_SpendTheGold_AndBumpStatsVersion()
    {
        var world = new World(new Vector2(128, 128)) { Wind = Vector2.Zero };
        world.AddIsland(new Island(1, new[] { new Vector2(40, 26), new Vector2(48, 26), new Vector2(48, 34), new Vector2(40, 34) }, hasShipyard: true));
        var ship = world.SpawnShip(new Vector2(37, 30), 0f, ShipStats.Sloop, PlayerId);
        world.Players[PlayerId].Gold = 50;
        world.Enqueue(new ToggleAnchorCommand(PlayerId));
        world.Step();
        var version = ship.StatsVersion;

        world.Enqueue(new PurchaseUpgradeCommand(PlayerId, "speed"));
        var events = StepAndDrain(world);

        var bought = Assert.Single(events.OfType<UpgradePurchased>());
        Assert.Equal(("speed", 1), (bought.UpgradeId, bought.Level));
        Assert.Contains(events.OfType<GoldChanged>(), e => e.Delta < 0 && e.Gold == world.Players[PlayerId].Gold);
        Assert.True(ship.StatsVersion > version);
    }

    [Fact]
    public void Groundings_AreAnnounced()
    {
        var world = new World(new Vector2(128, 128)) { Wind = Vector2.Zero };
        world.AddIsland(new Island(1, new[] { new Vector2(40, 26), new Vector2(50, 26), new Vector2(50, 34), new Vector2(40, 34) }));
        var ship = world.SpawnShip(new Vector2(30, 30), 0f, ShipStats.Sloop, PlayerId);
        ship.Throttle = ShipMovement.ThrottleLevels;
        ship.Speed = ship.CruiseSpeed;

        var events = StepAndDrain(world, SimConstants.TickRate * 5);

        Assert.Single(events.OfType<ShipGrounded>(), e => e.ShipId == ship.Id);
    }

    [Fact]
    public void Waves_AnnounceThemselvesAndTheirPirates()
    {
        var world = new World(new Vector2(192, 192)) { Waves = new WaveDirector(1) };
        world.SpawnShip(new Vector2(96, 96), 0f, ShipStats.Sloop, PlayerId).IsAnchored = true;
        world.DrainEvents();

        var events = StepAndDrain(world, (int)(WaveDirector.FirstWaveDelaySeconds * SimConstants.TickRate) + 1);

        var wave = Assert.Single(events.OfType<WaveStarted>());
        Assert.Equal((1, WaveDirector.FirstWaveSize), (wave.Wave, wave.Pirates));
        Assert.Equal(WaveDirector.FirstWaveSize, events.OfType<ShipSpawned>().Count());
    }

    [Theory]
    [MemberData(nameof(Rejections))]
    public void RejectedCommands_SayWhy(string scenario, Func<World, Ship, Command> setup, RejectionReason expected)
    {
        var (world, ship) = CreateWorld();
        var command = setup(world, ship);

        world.Enqueue(command);
        var events = StepAndDrain(world);

        var rejected = Assert.Single(events.OfType<CommandRejected>());
        Assert.True(expected == rejected.Reason, $"{scenario}: got {rejected.Reason}");
        Assert.Equal(PlayerId, rejected.PlayerId);
        Assert.Same(command, rejected.Command);
    }

    public static IEnumerable<object[]> Rejections() => new List<object[]>
    {
        new object[] { "sunk", (Func<World, Ship, Command>)((w, s) => { s.Health = 0; w.Step(); return new MoveCommand(PlayerId, Vector2.One); }), RejectionReason.NoShip },
        new object[] { "move at anchor", (Func<World, Ship, Command>)((w, s) => { s.IsAnchored = true; return new MoveCommand(PlayerId, Vector2.One); }), RejectionReason.Anchored },
        new object[] { "anchor mid-raise", (Func<World, Ship, Command>)((w, s) => { s.IsAnchored = true; Anchoring.Toggle(s); return new ToggleAnchorCommand(PlayerId); }), RejectionReason.AnchorBusy },
        new object[] { "empty slot", (Func<World, Ship, Command>)((w, s) => new CastAbilityCommand(PlayerId, AbilitySlot.Three, Vector2.Zero)), RejectionReason.EmptySlot },
        new object[] { "bad slot", (Func<World, Ship, Command>)((w, s) => new CastAbilityCommand(PlayerId, (AbilitySlot)42, Vector2.Zero)), RejectionReason.InvalidSlot },
        new object[] { "on cooldown", (Func<World, Ship, Command>)((w, s) => { w.TryCastAbility(s, AbilitySlot.One, Vector2.Zero); return new CastAbilityCommand(PlayerId, AbilitySlot.One, Vector2.Zero); }), RejectionReason.OnCooldown },
        new object[] { "shop at sea", (Func<World, Ship, Command>)((w, s) => new PurchaseUpgradeCommand(PlayerId, "speed")), RejectionReason.NotAtShipyard },
        new object[] { "plunder at sea", (Func<World, Ship, Command>)((w, s) => new ChoosePlunderCommand(PlayerId)), RejectionReason.NotAtShipyard },
    };

    [Fact]
    public void ShipyardRejections_SayWhy()
    {
        var world = new World(new Vector2(128, 128)) { Wind = Vector2.Zero };
        var yard = new Island(1, new[] { new Vector2(40, 26), new Vector2(48, 26), new Vector2(48, 34), new Vector2(40, 34) }, hasShipyard: true);
        world.AddIsland(yard);
        world.SpawnShip(new Vector2(37, 30), 0f, ShipStats.Sloop, PlayerId);
        world.Enqueue(new ToggleAnchorCommand(PlayerId));
        world.Step();
        world.StartPlunderCooldown(yard, 100);
        world.DrainEvents();

        world.Enqueue(new PurchaseUpgradeCommand(PlayerId, "speed"));      // no gold
        world.Enqueue(new PurchaseUpgradeCommand(PlayerId, "cannon-oil")); // not a thing
        world.Enqueue(new ChoosePlunderCommand(PlayerId));                 // island resting
        var reasons = StepAndDrain(world).OfType<CommandRejected>().Select(e => e.Reason).ToList();

        Assert.Equal(new[] { RejectionReason.NotEnoughGold, RejectionReason.UnknownUpgrade, RejectionReason.IslandOnCooldown }, reasons);
    }

    [Fact]
    public void AcceptedCommands_ProduceNoRejection()
    {
        var (world, _) = CreateWorld();

        world.Enqueue(new AdjustThrottleCommand(PlayerId, 2));
        world.Enqueue(new SetRudderCommand(PlayerId, 1));
        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.One, Vector2.Zero));

        Assert.Empty(StepAndDrain(world).OfType<CommandRejected>());
    }

    [Fact]
    public void NpcStance_MirrorsTheHuntersState()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(100, 100), 0f, ShipStats.Sloop, PlayerId);
        var pirate = world.SpawnShip(new Vector2(115, 100), MathF.PI, ShipStats.Sloop, abilities: Loadouts.Sloop); // inside aggro range
        var hunter = new HunterBehavior(new Vector2(130, 100));
        pirate.Behavior = hunter;
        player.IsAnchored = true;

        world.Step();
        Assert.Equal((HunterState.Hunting, NpcStance.Hunting), (hunter.State, pirate.Stance));

        player.Position = new Vector2(20, 20);
        world.Step();
        Assert.Equal((HunterState.Returning, NpcStance.Returning), (hunter.State, pirate.Stance));

        for (var t = 0; t < SimConstants.TickRate * 20 && hunter.State != HunterState.Guarding; t++)
            world.Step();
        Assert.Equal((HunterState.Guarding, NpcStance.Guarding), (hunter.State, pirate.Stance));
        Assert.Equal(NpcStance.None, player.Stance);
    }
}
