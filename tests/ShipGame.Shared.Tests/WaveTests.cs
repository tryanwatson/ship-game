using System.Numerics;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

public class WaveTests
{
    private const int PlayerId = 1;

    private static (World world, Ship player, WaveDirector waves) CreateWorld(int seed = 7)
    {
        var waves = new WaveDirector(seed);
        var world = new World(new Vector2(192, 192)) { Waves = waves };
        var player = world.SpawnShip(new Vector2(96, 96), 0f, ShipStats.Sloop, PlayerId);
        player.IsAnchored = true;
        return (world, player, waves);
    }

    private static List<Ship> Pirates(World world) => world.Ships.Where(s => s.Team == Team.Pirates).ToList();

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    private static void SinkAllPirates(World world)
    {
        foreach (var pirate in Pirates(world))
            pirate.Health = 0f;
        world.Step(); // removed this tick; the intermission countdown starts
    }

    [Fact]
    public void FirstWave_ArrivesAfterDelay()
    {
        var (world, _, waves) = CreateWorld();
        var delay = (int)(WaveDirector.FirstWaveDelaySeconds * SimConstants.TickRate);

        RunTicks(world, delay);
        Assert.Empty(Pirates(world));
        Assert.Equal(0, waves.Wave);

        world.Step();
        Assert.Equal(1, waves.Wave);
        Assert.Equal(WaveDirector.FirstWaveSize, Pirates(world).Count);
        Assert.All(Pirates(world), p => Assert.IsType<HunterBehavior>(p.Behavior));
    }

    [Fact]
    public void NextWave_WaitsUntilTheCurrentOneIsSunk_ThenGrows()
    {
        var (world, _, waves) = CreateWorld();
        RunTicks(world, (int)(WaveDirector.FirstWaveDelaySeconds * SimConstants.TickRate) + 1);

        RunTicks(world, SimConstants.TickRate * 15); // pirates still afloat (target anchored, may be sinking it)
        Assert.Equal(1, waves.Wave);

        SinkAllPirates(world);
        RunTicks(world, (int)(WaveDirector.IntermissionSeconds * SimConstants.TickRate) + 1);

        Assert.Equal(2, waves.Wave);
        Assert.Equal(WaveDirector.WaveSize(2), Pirates(world).Count);
        Assert.Equal(WaveDirector.FirstWaveSize + 1, WaveDirector.WaveSize(2));
    }

    [Fact]
    public void WaveSize_IsCapped()
    {
        Assert.Equal(WaveDirector.MaxWaveSize, WaveDirector.WaveSize(100));
    }

    [Fact]
    public void LaterWaves_AreTougher()
    {
        var (world, _, waves) = CreateWorld();
        for (var wave = 1; wave <= 3; wave++)
        {
            while (waves.Wave < wave)
                world.Step();
            if (wave < 3)
                SinkAllPirates(world);
        }

        var pirate = Pirates(world)[0];
        Assert.Equal(ShipStats.Sloop.MaxHealth * (1f + 2 * WaveDirector.HealthPerWave), pirate.Stats.MaxHealth, 3);
        Assert.Equal(pirate.Stats.MaxHealth, pirate.Health, 3); // spawns at full health
        Assert.Equal(1f + 2 * WaveDirector.CooldownSpeedPerWave, pirate.Stats.CooldownSpeed, 4);
        Assert.Equal(ShipStats.Sloop.MaxSpeed * (1f + 2 * WaveDirector.SpeedPerWave), pirate.Stats.MaxSpeed, 4);
    }

    [Fact]
    public void Pirates_SpawnInsideTheMap_FarFromPlayers_AndSpreadOut()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var (world, player, waves) = CreateWorld(seed);
            for (var wave = 1; wave <= 4; wave++)
            {
                while (waves.Wave < wave)
                    world.Step();

                var pirates = Pirates(world);
                foreach (var pirate in pirates)
                {
                    Assert.InRange(pirate.Position.X, 0f, world.WorldSize.X);
                    Assert.InRange(pirate.Position.Y, 0f, world.WorldSize.Y);
                    var distance = Vector2.Distance(pirate.Position, player.Position);
                    Assert.True(distance >= 20f, $"seed {seed} wave {wave}: spawned on top of the player");
                    Assert.True(distance <= WaveDirector.MaxSpawnDistance + 0.01f, $"seed {seed} wave {wave}: spawned {distance} tiles away");
                }
                for (var i = 0; i < pirates.Count; i++)
                    for (var j = i + 1; j < pirates.Count; j++)
                        Assert.True(Vector2.Distance(pirates[i].Position, pirates[j].Position) >= 2f, $"seed {seed} wave {wave}: stacked spawns");

                SinkAllPirates(world);
            }
        }
    }

    [Fact]
    public void Pirates_SpawnNearAPlayerInACorner_StillInsideTheMap()
    {
        var waves = new WaveDirector(3);
        var world = new World(new Vector2(192, 192)) { Waves = waves };
        var player = world.SpawnShip(new Vector2(4, 4), 0f, ShipStats.Sloop, PlayerId);
        player.IsAnchored = true;

        while (waves.Wave < 1)
            world.Step();

        foreach (var pirate in Pirates(world))
        {
            Assert.InRange(pirate.Position.X, 0f, 192f);
            Assert.InRange(pirate.Position.Y, 0f, 192f);
            Assert.InRange(Vector2.Distance(pirate.Position, player.Position), 20f, WaveDirector.MaxSpawnDistance + 0.01f);
        }
    }

    [Fact]
    public void SameSeed_SameSpawns()
    {
        var (worldA, _, wavesA) = CreateWorld(seed: 42);
        var (worldB, _, wavesB) = CreateWorld(seed: 42);
        while (wavesA.Wave < 1) worldA.Step();
        while (wavesB.Wave < 1) worldB.Step();

        Assert.Equal(Pirates(worldA).Select(p => p.Position), Pirates(worldB).Select(p => p.Position));
    }
}
