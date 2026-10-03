using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Tests;

public class RaidTests
{
    private const int PlayerId = 1;

    /// <summary>A run with waves and raids and one anchored player who can't be sunk, so the run outlasts the test.</summary>
    private static (World world, Ship player, WaveDirector waves) CreateRun(int players = 1)
    {
        var waves = new WaveDirector(seed: 7);
        var world = new World(new Vector2(192, 192)) { Waves = waves };
        Ship first = null!;
        for (var id = 1; id <= players; id++)
        {
            var ship = world.SpawnShip(new Vector2(90 + id * 4, 96), 0f, ShipStats.Sloop, id);
            ship.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 1_000_000f, "test"));
            ship.IsAnchored = true;
            first ??= ship;
        }
        return (world, first, waves);
    }

    private static List<Ship> Raiders(World world) => world.Ships.Where(WaveDirector.IsRaider).ToList();

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    [Fact]
    public void Raids_ComeAMinuteAfterTheLastIsSunk_OneMoreRaiderEachTime()
    {
        var (world, _, waves) = CreateRun();

        RunTicks(world, WaveDirector.RaidIntervalTicks - 1);
        Assert.Empty(Raiders(world));

        world.Step();
        Assert.Equal(1, waves.Raid);
        var first = Assert.Single(Raiders(world));

        // While the raider is afloat the countdown waits, however long it takes.
        RunTicks(world, WaveDirector.RaidIntervalTicks * 2);
        Assert.Equal(1, waves.Raid);
        Assert.Equal(WaveDirector.RaidIntervalTicks, waves.TicksUntilNextRaid);
        Assert.Equal(1, waves.RaidersLeft);

        // Sunk: a minute later, the next raid, one raider bigger.
        first.Health = 0f;
        world.Step();
        Assert.Equal(0, waves.RaidersLeft);
        RunTicks(world, WaveDirector.RaidIntervalTicks - 2);
        Assert.Empty(Raiders(world));
        world.Step();
        Assert.Equal(2, waves.Raid);
        Assert.Equal(WaveDirector.RaidSize(2), Raiders(world).Count);
        Assert.Equal(2, WaveDirector.RaidSize(2));
    }

    [Fact]
    public void Raiders_SetOutUnderSail_Hunting()
    {
        var (world, _, _) = CreateRun();

        RunTicks(world, WaveDirector.RaidIntervalTicks);

        var raider = Assert.Single(Raiders(world));
        Assert.False(raider.IsAnchored);
        Assert.Equal(NpcStance.Hunting, raider.Stance);
        Assert.True(raider.Throttle > 0);
    }

    [Fact]
    public void Forecast_SaysWhatsComing_AndHowMany()
    {
        var (world, _, waves) = CreateRun(players: 2);

        world.Step();
        var before = waves.Status;
        Assert.Equal(0, before.Wave);
        Assert.Equal(0, before.WavePiratesLeft);
        Assert.Equal(WaveDirector.WaveSize(1, players: 2), before.NextWaveSize);
        Assert.Equal(WaveDirector.RaidSize(1, players: 2), before.NextRaidSize);
        Assert.Equal(WaveDirector.RaidIntervalTicks - 1, before.TicksUntilNextRaid);

        // Once the first raid is in, the wave's count leaves the raiders out, and the forecast moves on.
        RunTicks(world, WaveDirector.RaidIntervalTicks);
        var after = waves.Status;
        var guards = world.Ships.Count(s => s.Team == Team.Pirates && !WaveDirector.IsRaider(s));
        Assert.Equal(guards, after.WavePiratesLeft);
        Assert.NotEmpty(Raiders(world));
        Assert.Equal(WaveDirector.WaveSize(after.Wave + 1, players: 2), after.NextWaveSize);
        Assert.Equal(1, after.Raid);
        Assert.Equal(Raiders(world).Count, after.RaidersLeft);
        Assert.Equal(WaveDirector.RaidSize(2, players: 2), after.NextRaidSize);
    }

    [Fact]
    public void RaidSize_GrowsWithTheCrew_AndIsCapped()
    {
        // Each player beyond the first adds half: three players make raids twice the size.
        Assert.Equal(1, WaveDirector.RaidSize(1, players: 1));
        Assert.Equal(2, WaveDirector.RaidSize(1, players: 3));
        Assert.Equal(20, WaveDirector.RaidSize(10, players: 3));
        Assert.Equal(WaveDirector.AbsoluteMaxWaveSize, WaveDirector.RaidSize(100, players: 12));
    }

    [Fact]
    public void Raiders_DontHoldUpTheWaves()
    {
        var (world, _, waves) = CreateRun();
        RunTicks(world, WaveDirector.RaidIntervalTicks);
        Assert.NotEmpty(Raiders(world));
        var wave = waves.Wave;

        // Sink the wave's guards, leave the raider: the next wave still comes after the intermission.
        foreach (var guard in world.Ships.Where(s => s.Team == Team.Pirates && !WaveDirector.IsRaider(s)))
            guard.Health = 0f;
        RunTicks(world, (int)(WaveDirector.IntermissionSeconds * SimConstants.TickRate) + 2);

        Assert.Equal(wave + 1, waves.Wave);
        Assert.NotEmpty(Raiders(world));
    }

    [Fact]
    public void Raider_GoesForTheNearestPlayer_FromAcrossTheMap_AndNeverGivesUp()
    {
        var world = new World(new Vector2(256, 256)) { Wind = Vector2.Zero };
        var near = world.SpawnShip(new Vector2(130, 128), 0f, ShipStats.Sloop, 1);
        var far = world.SpawnShip(new Vector2(240, 128), 0f, ShipStats.Sloop, 2);
        foreach (var player in new[] { near, far })
        {
            player.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 1_000_000f, "test"));
            player.IsAnchored = true;
        }
        var raider = world.SpawnShip(new Vector2(20, 128), 0f, ShipStats.Sloop, abilities: Loadouts.Pirate);
        var behavior = new HunterBehavior(raider.Position, relentless: true);
        raider.Behavior = behavior;
        raider.Throttle = ShipMovement.ThrottleLevels;
        var start = Vector2.Distance(raider.Position, near.Position);

        // 110 tiles off: far beyond a guard's aggro, disengage, and leash ranges.
        Assert.True(start > HunterBehavior.LeashRange * 2);
        RunTicks(world, SimConstants.TickRate * 20);

        Assert.Same(near, behavior.Target);
        Assert.Equal(HunterState.Hunting, behavior.State);
        Assert.True(Vector2.Distance(raider.Position, near.Position) < start - 60f, "the raider should have closed in");
    }

    [Fact]
    public void Raider_SwitchesToWhicheverPlayerIsNearest()
    {
        var world = new World(new Vector2(256, 256)) { Wind = Vector2.Zero };
        var a = world.SpawnShip(new Vector2(60, 128), 0f, ShipStats.Sloop, 1);
        var b = world.SpawnShip(new Vector2(200, 128), 0f, ShipStats.Sloop, 2);
        var raider = world.SpawnShip(new Vector2(110, 128), 0f, ShipStats.Sloop, abilities: Loadouts.Pirate);
        var behavior = new HunterBehavior(raider.Position, relentless: true);
        raider.Behavior = behavior;

        world.Step();
        Assert.Same(a, behavior.Target);

        b.Position = new Vector2(115, 128); // b sails close
        world.Step();
        Assert.Same(b, behavior.Target);
    }
}
