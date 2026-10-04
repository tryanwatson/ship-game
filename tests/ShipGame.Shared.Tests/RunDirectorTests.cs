using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Tests;

public class RunDirectorTests
{
    private const int PlayerId = 1;

    private static readonly int StormDelayTicks = (int)(RunDirector.StormDelaySeconds * SimConstants.TickRate);

    /// <summary>
    /// The full-length map's open water (no islands) with a director, and anchored players who can't be sunk, midway up
    /// it so the storm takes a long time to reach them.
    /// </summary>
    private static (World world, Ship player, RunDirector director) CreateRun(int players = 1, float y = 450f)
    {
        var world = new World(Archipelago.Size) { Wind = Vector2.Zero };
        var director = new RunDirector(seed: 7, world.WorldSize);
        world.Director = director;
        Ship first = null!;
        for (var id = 1; id <= players; id++)
        {
            var ship = world.SpawnShip(new Vector2(40 + id * 4, y), 0f, ShipStats.Sloop, id);
            Invulnerable(ship);
            ship.IsAnchored = true;
            first ??= ship;
        }
        return (world, first, director);
    }

    /// <summary>Puts the storm's edge at <paramref name="stormY"/>, already moving.</summary>
    private static void StormAt(RunDirector director, float stormY) =>
        director.Restore(director.Status with { StormY = stormY, TicksUntilStorm = 0 });

    private static void Invulnerable(Ship ship) =>
        ship.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 1_000_000f, "test"));

    private static List<Ship> Hunters(World world) => world.Ships.Where(RunDirector.IsHunter).ToList();

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    // ---- The storm --------------------------------------------------------------------------------------

    [Fact]
    public void Storm_FormsOffTheSouthernEdge_WaitsForTheOpening_ThenRollsNorth()
    {
        var (world, _, director) = CreateRun();
        var start = Archipelago.Size.Y + RunDirector.StormStartBeyondEdge;
        Assert.Equal(start, director.StormY);

        RunTicks(world, StormDelayTicks);
        Assert.Equal(start, director.StormY);

        RunTicks(world, SimConstants.TickRate * 30);
        Assert.Equal(start - RunDirector.StormSpeed * 30f, director.StormY, 1);
    }

    [Fact]
    public void Storm_DoesNoHarmByItself()
    {
        var (world, _, director) = CreateRun();
        var caught = world.SpawnShip(new Vector2(30, 800), 0f, ShipStats.Sloop);
        caught.IsAnchored = true;
        StormAt(director, 750f);

        RunTicks(world, SimConstants.TickRate * 2);

        Assert.Equal(ShipStats.Sloop.MaxHealth, caught.Health);
    }

    [Fact]
    public void Storm_StopsShortOfTheLastSea()
    {
        var (world, _, director) = CreateRun();
        var lastSea = Archipelago.Seas[^1];
        StormAt(director, lastSea.South + 1f);

        RunTicks(world, SimConstants.TickRate * 10);

        Assert.Equal(lastSea.South, director.StormY);
    }

    // ---- Its bounty hunters -----------------------------------------------------------------------------

    [Fact]
    public void NoHunters_WhileNobodyIsInTheStorm()
    {
        var (world, player, director) = CreateRun();
        StormAt(director, player.Position.Y + 20f);

        RunTicks(world, RunDirector.FirstHunterTicks + RunDirector.HunterIntervalTicks * 3);

        Assert.Empty(Hunters(world));
    }

    [Fact]
    public void Hunters_ComeForAPlayerInTheStorm_MoreTheLongerTheyStay_UpToTheCap()
    {
        var (world, player, director) = CreateRun();
        StormAt(director, player.Position.Y - 60f); // deep in it; it won't pass us by during the test

        RunTicks(world, RunDirector.FirstHunterTicks - 1);
        Assert.Empty(Hunters(world));
        world.Step();
        Assert.Single(Hunters(world));

        RunTicks(world, RunDirector.HunterIntervalTicks);
        Assert.Equal(2, Hunters(world).Count);

        RunTicks(world, RunDirector.HunterIntervalTicks * (RunDirector.MaxHuntersPerPlayer + 3));
        Assert.Equal(RunDirector.MaxHuntersPerPlayer, Hunters(world).Count);
        Assert.Equal(RunDirector.MaxHuntersPerPlayer, director.Hunters);
    }

    [Fact]
    public void Hunters_AreStrongForTheSea_AndSetOutUnderSail_FromInsideTheStorm()
    {
        var (world, player, director) = CreateRun();
        StormAt(director, player.Position.Y - 10f);

        RunTicks(world, RunDirector.FirstHunterTicks);

        var hunter = Assert.Single(Hunters(world));
        Assert.True(director.InStorm(hunter.Position));
        Assert.True(hunter.Position.Y > player.Position.Y, "hunters come up from behind");
        Assert.Equal(Archipelago.LevelAt(hunter.Position) + RunDirector.HunterLevelAboveSea, hunter.Level);
        Assert.Equal(RunDirector.HunterLevel(Archipelago.LevelAt(hunter.Position)), hunter.Level);
        Assert.Equal(ShipStats.PirateSloop.MaxHealth * (1f + PirateLevels.HealthPerLevel * (hunter.Level - 1)), hunter.Stats.MaxHealth, 3);
        Assert.False(hunter.IsAnchored);
        Assert.Equal(NpcStance.Hunting, hunter.Stance);
        Assert.True(hunter.Throttle > 0);
    }

    [Fact]
    public void Hunters_IgnorePlayersOutOfTheStorm_AndMeltAwayWithNoOneToChase()
    {
        var (world, player, director) = CreateRun();
        StormAt(director, player.Position.Y - 10f);
        RunTicks(world, RunDirector.FirstHunterTicks);
        var hunter = Assert.Single(Hunters(world));
        var behavior = (HunterBehavior)hunter.Behavior!;
        world.Step();
        Assert.Same(player, behavior.Target);

        // The player slips out north of the storm: the hunter loses interest, and before long is gone.
        player.Position = player.PreviousPosition = new Vector2(player.Position.X, director.StormY - 20f);
        world.Step();
        Assert.Null(behavior.Target);
        RunTicks(world, RunDirector.HunterIdleTicks);
        Assert.Empty(Hunters(world));
    }

    [Fact]
    public void Hunters_ThatLeaveTheStorm_MeltBackIntoIt()
    {
        var (world, player, director) = CreateRun();
        StormAt(director, player.Position.Y - 10f);
        RunTicks(world, RunDirector.FirstHunterTicks);
        var hunter = Assert.Single(Hunters(world));

        hunter.Position = new Vector2(hunter.Position.X, director.StormY - RunDirector.VanishBeyondEdge - 1f);
        world.Step();

        Assert.Null(world.FindShip(hunter.Id));
    }

    [Fact]
    public void Hunters_SpawnInsideTheMapAndTheStorm_ClearOfPlayers()
    {
        foreach (var y in new[] { 120f, 450f, 890f }) // near the north end, mid-map, and hard against the southern edge
        {
            for (var seed = 0; seed < 10; seed++)
            {
                var world = new World(Archipelago.Size) { Wind = Vector2.Zero };
                var director = new RunDirector(seed, world.WorldSize);
                world.Director = director;
                for (var id = 1; id <= 4; id++)
                {
                    var ship = world.SpawnShip(new Vector2(30 + id * 8, y), 0f, ShipStats.Sloop, id);
                    Invulnerable(ship);
                    ship.IsAnchored = true;
                }
                StormAt(director, y - 15f);
                RunTicks(world, RunDirector.FirstHunterTicks);

                var hunters = Hunters(world);
                Assert.Equal(4, hunters.Count);
                foreach (var hunter in hunters)
                {
                    Assert.InRange(hunter.Position.X, 0f, world.WorldSize.X);
                    Assert.InRange(hunter.Position.Y, 0f, world.WorldSize.Y);
                    Assert.True(director.InStorm(hunter.Position), $"y {y} seed {seed}: spawned out of the storm");
                    var nearest = world.Ships.Where(s => s.OwnerPlayerId is not null).Min(s => Vector2.Distance(s.Position, hunter.Position));
                    Assert.True(nearest >= 6f, $"y {y} seed {seed}: spawned {nearest} tiles from a player");
                }
            }
        }
    }

    [Fact]
    public void SameSeed_SameHunters()
    {
        var (worldA, playerA, directorA) = CreateRun();
        var (worldB, _, directorB) = CreateRun();
        StormAt(directorA, playerA.Position.Y - 10f);
        StormAt(directorB, playerA.Position.Y - 10f);
        RunTicks(worldA, RunDirector.FirstHunterTicks);
        RunTicks(worldB, RunDirector.FirstHunterTicks);

        Assert.Equal(Hunters(worldA).Select(p => p.Position), Hunters(worldB).Select(p => p.Position));
    }

    [Fact]
    public void Hunters_StopOnceTheRunIsOver()
    {
        var (world, player, director) = CreateRun();
        StormAt(director, player.Position.Y - 10f);
        world.EndRun();

        RunTicks(world, RunDirector.FirstHunterTicks * 2);

        Assert.Empty(Hunters(world));
    }

    [Fact]
    public void Respawns_AreNeverInsideTheStorm()
    {
        var (world, player, director) = CreateRun(players: 2);
        var other = world.GetPlayerShip(2)!;
        // The storm's edge just south of the survivor: half of the spots around them are in it.
        StormAt(director, player.Position.Y + 1f);
        other.Health = 0f;

        RunTicks(world, Respawning.DelayTicks + 1);

        var back = world.GetPlayerShip(2)!;
        Assert.False(director.InStorm(back.Position));
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
