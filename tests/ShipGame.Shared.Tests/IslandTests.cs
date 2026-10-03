using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

public class IslandTests
{
    private const int PlayerId = 1;

    // A 10x8 block of land (80 sq tiles) spanning x 40..50, y 26..34.
    private static Island Block() => new(1, new[] { new Vector2(40, 26), new Vector2(50, 26), new Vector2(50, 34), new Vector2(40, 34) });

    private static World CreateWorld(params Island[] islands)
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        foreach (var island in islands)
            world.AddIsland(island);
        return world;
    }

    private static bool Overlaps(Ship ship, Island island)
    {
        Span<Vector2> hull = stackalloc Vector2[HullShape.PointCount];
        HullShape.GetWorldOutline(ship.Position, ship.Heading, ship.Stats, hull);
        return Geometry.TryGetPenetration(hull, island.Outline, out _, out var depth) && depth > 0.02f;
    }

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    [Fact]
    public void MapIslands_AreBetween30And100Tiles_AndLeaveTheStartClear()
    {
        var islands = Archipelago.CreateIslands();

        Assert.NotEmpty(islands);
        Assert.All(islands, island => Assert.InRange(island.Area, 30f, 100f));
        Assert.All(islands, island => Assert.True(island.DistanceTo(Archipelago.Size / 2f) > 15f));
        Assert.All(islands, island =>
        {
            Assert.InRange(island.Center.X - island.BoundingRadius, 0f, Archipelago.Size.X);
            Assert.InRange(island.Center.X + island.BoundingRadius, 0f, Archipelago.Size.X);
            Assert.InRange(island.Center.Y - island.BoundingRadius, 0f, Archipelago.Size.Y);
            Assert.InRange(island.Center.Y + island.BoundingRadius, 0f, Archipelago.Size.Y);
        });
    }

    [Fact]
    public void ShipsCannotSailThroughIslands()
    {
        var island = Block();
        var world = CreateWorld(island);
        var ship = world.SpawnShip(new Vector2(20, 30), 0f, ShipStats.Sloop, PlayerId);
        ship.Throttle = ShipMovement.ThrottleLevels;
        ship.Speed = ship.CruiseSpeed;

        for (var t = 0; t < SimConstants.TickRate * 15; t++)
        {
            world.Step();
            Assert.False(Overlaps(ship, island), $"tick {t}: hull inside the island at {ship.Position}");
        }

        Assert.True(ship.Position.X < 40f); // still on the near side, pinned against the shore
    }

    [Fact]
    public void HardImpact_DealsTenPercentOfStartingHealth_Once()
    {
        var world = CreateWorld(Block());
        var ship = world.SpawnShip(new Vector2(20, 30), 0f, ShipStats.Sloop, PlayerId);
        ship.Throttle = ShipMovement.ThrottleLevels;
        ship.Speed = ship.CruiseSpeed;

        RunTicks(world, SimConstants.TickRate * 15); // hit, then keep driving into the shore

        Assert.Equal(10f, IslandCollision.GroundingDamage);
        Assert.Equal(ship.Stats.MaxHealth - IslandCollision.GroundingDamage, ship.Health);
    }

    [Fact]
    public void GentleContact_DoesNoDamage()
    {
        var world = CreateWorld(Block());
        var ship = world.SpawnShip(new Vector2(37, 30), 0f, ShipStats.Sloop, PlayerId);
        ship.Throttle = 1; // 1 tile/s, never above the damaging impact speed

        RunTicks(world, SimConstants.TickRate * 10);

        Assert.Equal(ship.Stats.MaxHealth, ship.Health);
        Assert.True(ship.Position.X < 40f);
    }

    [Fact]
    public void GlancingBlow_ScrapesAlongTheShore()
    {
        var world = CreateWorld(Block());
        // Running nearly parallel to the south shore (y = 34), angled slightly into it.
        var ship = world.SpawnShip(new Vector2(36, 35.2f), -0.15f, ShipStats.Sloop, PlayerId);
        ship.Throttle = 3;
        ship.Speed = ship.CruiseSpeed;

        RunTicks(world, SimConstants.TickRate * 3);

        Assert.True(ship.Speed > ship.CruiseSpeed * 0.5f, $"speed {ship.Speed}");
        Assert.True(ship.Position.X > 41f); // made progress along the coast
    }

    [Fact]
    public void SteepScrape_CountsAsOneImpact()
    {
        // 30 degrees into the shore at full sail: hard enough to hurt, and the bow stays angled in as it scrapes.
        var world = CreateWorld(Block());
        var ship = world.SpawnShip(new Vector2(34, 36.5f), -MathF.PI / 6f, ShipStats.Sloop, PlayerId);
        ship.Throttle = ShipMovement.ThrottleLevels;
        ship.Speed = ship.CruiseSpeed;

        RunTicks(world, SimConstants.TickRate * 3);

        Assert.True(ship.IsAground);
        Assert.Equal(ship.Stats.MaxHealth - IslandCollision.GroundingDamage, ship.Health);
    }

    [Fact]
    public void WindCannotPushShipsOntoLand()
    {
        var island = Block();
        var world = new World(new Vector2(64, 64)) { Wind = new Vector2(1f, 0f) }; // blowing onto the west shore
        world.AddIsland(island);
        var ship = world.SpawnShip(new Vector2(36, 30), MathF.PI / 2f, ShipStats.Sloop, PlayerId);

        RunTicks(world, SimConstants.TickRate * 20);

        Assert.False(Overlaps(ship, island));
        Assert.Equal(ship.Stats.MaxHealth, ship.Health);
    }

    [Fact]
    public void Cannonballs_AreStoppedByLand()
    {
        var world = CreateWorld(new Island(1, new[] { new Vector2(28, 33), new Vector2(32, 33), new Vector2(32, 34), new Vector2(28, 34) }));
        var shooter = world.SpawnShip(new Vector2(30, 30), 0f, ShipStats.Sloop, PlayerId, Loadouts.FullArsenal);
        var target = world.SpawnShip(new Vector2(30, 37), 0f, ShipStats.Sloop); // behind the strip of land

        world.TryCastAbility(shooter, AbilitySlot.One, shooter.Position + new Vector2(0, 5));
        RunTicks(world, SimConstants.TickRate);

        Assert.Equal(target.Stats.MaxHealth, target.Health);
        Assert.Empty(world.Projectiles);
    }

    [Fact]
    public void Pirates_NeverSpawnOnOrAgainstLand()
    {
        for (var seed = 0; seed < 15; seed++)
        {
            var waves = new WaveDirector(seed);
            var world = new World(Archipelago.Size) { Waves = waves };
            foreach (var island in Archipelago.CreateIslands())
                world.AddIsland(island);
            world.SpawnShip(Archipelago.Size / 2f, 0f, ShipStats.Sloop, PlayerId).IsAnchored = true;

            for (var wave = 1; wave <= 4; wave++)
            {
                while (waves.Wave < wave)
                    world.Step();
                foreach (var pirate in world.Ships.Where(s => s.Team == Team.Pirates))
                    Assert.True(world.DistanceToLand(pirate.Position) >= 3.9f, $"seed {seed} wave {wave}: spawned {world.DistanceToLand(pirate.Position)} from land");
                foreach (var pirate in world.Ships.Where(s => s.Team == Team.Pirates).ToList())
                    pirate.Health = 0f;
                world.Step();
            }
        }
    }

    [Fact]
    public void Island_RejectsConcaveOutlines()
    {
        var concave = new[] { new Vector2(0, 0), new Vector2(4, 0), new Vector2(2, 1), new Vector2(4, 4), new Vector2(0, 4) };
        Assert.Throws<ArgumentException>(() => new Island(1, concave));
    }
}
