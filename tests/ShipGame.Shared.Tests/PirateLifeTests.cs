using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

/// <summary>Pirates between fights: guarding islands, roaming their seas, and sailing in groups.</summary>
public class PirateLifeTests
{
    private const int PlayerId = 1;

    // A 10x10 island at the middle of a 192x192 sea.
    private static Island Isle() => new(1, new[] { new Vector2(91, 91), new Vector2(101, 91), new Vector2(101, 101), new Vector2(91, 101) });

    private static Ship SpawnPirate(World world, Vector2 position, PirateOrders orders, int seed = 7, PirateGroup? group = null)
    {
        var pirate = world.SpawnShip(position, 0f, ShipStats.PirateSloop, abilities: Loadouts.Pirate);
        group?.Add(pirate);
        pirate.Behavior = new HunterBehavior(orders, seed, group);
        return pirate;
    }

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    [Fact]
    public void IslandGuard_CruisesRoundItsIsland_WithoutLeavingItOrRunningAground()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var island = Isle();
        world.AddIsland(island);
        var post = PirateCamps.GuardOrders(island);
        var pirate = SpawnPirate(world, island.Center + new Vector2(0, post.MinRadius + 1f), post);
        var start = pirate.Position;

        var furthestFromStart = 0f;
        for (var t = 0; t < SimConstants.TickRate * 90; t++)
        {
            world.Step();
            furthestFromStart = MathF.Max(furthestFromStart, Vector2.Distance(pirate.Position, start));
            Assert.True(Vector2.Distance(pirate.Position, island.Center) <= post.Radius + 4f, $"tick {t}: strayed to {pirate.Position}");
            Assert.True(world.DistanceToLand(pirate.Position) > 0.5f, $"tick {t}: ran aground at {pirate.Position}");
        }

        Assert.Equal(NpcStance.Patrolling, pirate.Stance);
        Assert.True(furthestFromStart > post.MinRadius, $"only got {furthestFromStart} from where it started");
        Assert.Equal(pirate.Stats.MaxHealth, pirate.Health);
    }

    [Fact]
    public void IslandGuard_GoesForATrespasser_ItCannotSeeItself()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var island = Isle();
        world.AddIsland(island);
        var post = PirateCamps.GuardOrders(island);
        var pirate = SpawnPirate(world, island.Center + new Vector2(0, -post.MinRadius - 1f), post);
        // Off the far side of the island: inside its watch, but out of the pirate's own sight.
        var player = world.SpawnShip(island.Center + new Vector2(0, post.Watch - 1f), 0f, ShipStats.Sloop, PlayerId);
        player.IsAnchored = true;
        Assert.True(Vector2.Distance(player.Position, pirate.Position) > HunterBehavior.AggroRange);

        world.Step();

        var behavior = (HunterBehavior)pirate.Behavior!;
        Assert.Equal(HunterState.Hunting, behavior.State);
        Assert.Same(player, behavior.Target);
    }

    [Fact]
    public void Rover_TravelsItsSea_AndStaysInIt()
    {
        var world = new World(new Vector2(96, 300)) { Wind = Vector2.Zero };
        var orders = new RoamOrders(100f, 200f);
        var pirate = SpawnPirate(world, new Vector2(48, 150), orders);

        var travelled = 0f;
        for (var t = 0; t < SimConstants.TickRate * 120; t++)
        {
            var before = pirate.Position;
            world.Step();
            travelled += Vector2.Distance(before, pirate.Position);
            Assert.InRange(pirate.Position.Y, orders.North - 6f, orders.South + 6f);
        }

        Assert.True(travelled > 100f, $"only sailed {travelled} tiles in two minutes");
        Assert.Equal(NpcStance.Patrolling, pirate.Stance);
    }

    [Fact]
    public void Rover_LeashedFromWhereTheChaseBegan_GoesBackToRoaming()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var pirate = SpawnPirate(world, new Vector2(100, 100), new RoamOrders(20f, 170f));
        var player = world.SpawnShip(new Vector2(110, 100), 0f, ShipStats.Sloop, PlayerId);
        player.Health = 1e6f;
        var behavior = (HunterBehavior)pirate.Behavior!;
        world.Step();
        Assert.Equal(HunterState.Hunting, behavior.State);
        Assert.Equal(new Vector2(100, 100), behavior.Home);

        player.Position = new Vector2(10, 10); // gets away
        world.Step();
        Assert.Equal(HunterState.Returning, behavior.State);

        for (var t = 0; t < SimConstants.TickRate * 20 && behavior.State != HunterState.Patrolling; t++)
            world.Step();
        Assert.Equal(HunterState.Patrolling, behavior.State);
        Assert.NotNull(behavior.Waypoint);
    }

    [Fact]
    public void Group_JoinsTheFight_WhenOneOfItIsShot()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var group = new PirateGroup();
        var orders = new RoamOrders(20f, 170f);
        var members = Enumerable.Range(0, 3).Select(i => SpawnPirate(world, new Vector2(100 + 5 * i, 100), orders, seed: i, group)).ToList();
        // Well out of everyone's sight.
        var player = world.SpawnShip(new Vector2(100, 100 + HunterBehavior.AggroRange + 10f), 0f, ShipStats.Sloop, PlayerId);
        player.IsAnchored = true;
        world.Step();
        Assert.All(members, m => Assert.Equal(NpcStance.Patrolling, m.Stance));

        members[2].LastHitByShipId = player.Id;
        members[2].LastHitTick = world.Tick;
        RunTicks(world, 3);

        Assert.All(members, m =>
        {
            var behavior = (HunterBehavior)m.Behavior!;
            Assert.Equal(HunterState.Hunting, behavior.State);
            Assert.Same(player, behavior.Target);
        });
    }

    [Fact]
    public void Group_FollowsItsLeader_AndCarriesOnWhenItSinks()
    {
        var world = new World(new Vector2(96, 300)) { Wind = Vector2.Zero };
        var group = new PirateGroup();
        var orders = new RoamOrders(60f, 240f);
        var members = Enumerable.Range(0, 3).Select(i => SpawnPirate(world, new Vector2(40 + 4 * i, 150), orders, seed: i, group)).ToList();

        RunTicks(world, SimConstants.TickRate * 60);
        Assert.Same(members[0], group.Leader(world));
        Assert.All(members.Skip(1), m =>
            Assert.True(Vector2.Distance(m.Position, members[0].Position) < 15f, $"follower at {m.Position}, leader at {members[0].Position}"));

        members[0].Health = 0f;
        RunTicks(world, 2);
        Assert.Same(members[1], group.Leader(world));
        var start = members[1].Position;
        RunTicks(world, SimConstants.TickRate * 30);
        Assert.True(Vector2.Distance(members[1].Position, start) > 10f, "the new leader should carry on roaming");
        Assert.True(Vector2.Distance(members[2].Position, members[1].Position) < 15f);
    }

    [Fact]
    public void Populate_MixesGuardsRoversAndGroups_FromTheSeed()
    {
        var crew = new List<(int, Ability)> { (PlayerId, new BroadsideVolley()) };
        var world = Runs.Create(seed: 1, crew);
        var behaviors = world.Ships.Where(s => s.Team == Team.Pirates && !s.IsBoss).Select(s => (HunterBehavior)s.Behavior!).ToList();

        Assert.Contains(behaviors, b => b.Orders is GuardPost { IslandId: not null });
        Assert.Contains(behaviors, b => b.Orders is RoamOrders);
        Assert.Contains(behaviors, b => b.Group is { MemberIds.Count: > 1 });
        Assert.Contains(behaviors, b => b.Group is null);
        Assert.All(behaviors, b => Assert.True(b.Group is null || b.Group.MemberIds.Count <= PirateCamps.MaxGroupSize));

        // Nobody guards a shipyard, and nobody roams down to the start.
        Assert.All(behaviors.Select(b => b.Orders).OfType<GuardPost>(), p => Assert.False(world.FindIsland(p.IslandId!.Value)!.HasShipyard));
        Assert.All(behaviors.Select(b => b.Orders).OfType<RoamOrders>(), r => Assert.True(r.South <= Archipelago.Start.Y - PirateCamps.StartBerth));

        // The same seed deals the same hand; another deals a different one.
        static string Hand(World w) => string.Join(";", w.Ships.Where(s => s.Team == Team.Pirates)
            .Select(s => (HunterBehavior)s.Behavior!).Select(b => $"{b.Orders}/{b.Group?.MemberIds.Count ?? 1}"));
        Assert.Equal(Hand(world), Hand(Runs.Create(seed: 1, crew)));
        Assert.NotEqual(Hand(world), Hand(Runs.Create(seed: 2, crew)));
    }

    [Fact]
    public void Flagship_PatrolsItsWaters()
    {
        var crew = new List<(int, Ability)> { (PlayerId, new BroadsideVolley()) };
        var world = Runs.Create(seed: 1, crew);
        var flagship = world.Ships.Single(s => s.IsBoss);

        RunTicks(world, SimConstants.TickRate * 20);

        Assert.True(Vector2.Distance(flagship.Position, Archipelago.BossPosition) > 1f, "the flagship should be under way");
        Assert.True(Vector2.Distance(flagship.Position, Archipelago.BossPosition) <= PirateCamps.FlagshipPatrolRadius + 4f);
    }
}
