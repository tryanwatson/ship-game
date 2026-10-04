using System.Numerics;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

public class AnchorAndPlunderTests
{
    private const int PlayerId = 1;

    // An 8x8 island (64 sq tiles) spanning x 40..48, y 26..34.
    private static Island Isle(int id = 1, float x = 40f) =>
        new(id, new[] { new Vector2(x, 26), new Vector2(x + 8, 26), new Vector2(x + 8, 34), new Vector2(x, 34) });

    private static (World world, Ship ship) CreateWorld(Vector2 shipAt, params Island[] islands)
    {
        var world = new World(new Vector2(128, 128)); // default wind on: anchoring must beat it
        foreach (var island in islands)
            world.AddIsland(island);
        var ship = world.SpawnShip(shipAt, 0f, ShipStats.Sloop, PlayerId);
        return (world, ship);
    }

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    private static int Gold(World world) => world.Players[PlayerId].Gold;

    [Fact]
    public void DroppingAnchor_StopsTheShipInstantly_AndHoldsItAgainstTheWind()
    {
        var (world, ship) = CreateWorld(new Vector2(20, 64));
        ship.Throttle = ShipMovement.ThrottleLevels;
        ship.Speed = ship.CruiseSpeed;

        world.Enqueue(new AnchorKeyCommand(PlayerId, true));
        RunTicks(world, Anchoring.DropTicks + 1);
        var anchoredAt = ship.Position;
        Assert.Equal(AnchorState.Down, ship.Anchor);
        Assert.Equal(0f, ship.Speed);
        Assert.Equal(0, ship.Throttle); // sails furled as she brings up

        world.Enqueue(new SetRudderCommand(PlayerId, 1));
        world.Enqueue(new MoveCommand(PlayerId, new Vector2(100, 100)));
        world.Enqueue(new AdjustThrottleCommand(PlayerId, 3));
        RunTicks(world, SimConstants.TickRate * 10);

        Assert.Equal(anchoredAt, ship.Position);
        Assert.Equal(0f, ship.Heading);
        Assert.Null(ship.MoveTarget);
        Assert.Equal(0, ship.Throttle);
    }

    [Fact]
    public void LettingGo_TakesHoldingTheKeyForTwoSeconds_WhileTheShipSailsOn()
    {
        var (world, ship) = CreateWorld(new Vector2(20, 64));
        ship.Throttle = ShipMovement.ThrottleLevels;
        ship.Speed = ship.CruiseSpeed;
        var start = ship.Position;

        world.Enqueue(new AnchorKeyCommand(PlayerId, true));
        RunTicks(world, Anchoring.DropTicks);
        Assert.Equal(AnchorState.Weighed, ship.Anchor);
        Assert.True(Anchoring.DropProgress(ship) > 0.95f);
        Assert.True(ship.Position.X > start.X + 1f, "should keep sailing while the anchor is let go");

        world.Step();
        Assert.Equal(AnchorState.Down, ship.Anchor);
        Assert.Equal(0f, Anchoring.DropProgress(ship));
    }

    [Fact]
    public void ReleasingTheKeyEarly_KeepsTheAnchorUp_AndAFreshPressStartsOver()
    {
        var (world, ship) = CreateWorld(new Vector2(20, 64));
        world.Enqueue(new AnchorKeyCommand(PlayerId, true));
        RunTicks(world, Anchoring.DropTicks - 5);
        world.Enqueue(new AnchorKeyCommand(PlayerId, false));
        RunTicks(world, Anchoring.DropTicks);
        Assert.Equal(AnchorState.Weighed, ship.Anchor);

        world.Enqueue(new AnchorKeyCommand(PlayerId, true));
        RunTicks(world, Anchoring.DropTicks - 5);
        Assert.Equal(AnchorState.Weighed, ship.Anchor); // the earlier hold doesn't count
        RunTicks(world, 6);
        Assert.Equal(AnchorState.Down, ship.Anchor);
    }

    [Fact]
    public void RepeatedPresses_DoNotRestartOrShortenTheHold()
    {
        var (world, ship) = CreateWorld(new Vector2(20, 64));
        world.Enqueue(new AnchorKeyCommand(PlayerId, true));
        RunTicks(world, 10);
        world.Enqueue(new AnchorKeyCommand(PlayerId, true)); // a client sending presses without releases
        RunTicks(world, Anchoring.DropTicks - 10);
        Assert.Equal(AnchorState.Weighed, ship.Anchor);
        world.Step();
        Assert.Equal(AnchorState.Down, ship.Anchor);
    }

    [Fact]
    public void HoldingTheKeyAfterTheDrop_DoesNotStartRaising()
    {
        var (world, ship) = CreateWorld(new Vector2(20, 64));
        world.Enqueue(new AnchorKeyCommand(PlayerId, true));
        RunTicks(world, Anchoring.DropTicks + SimConstants.TickRate * 3);
        Assert.Equal(AnchorState.Down, ship.Anchor);

        world.Enqueue(new AnchorKeyCommand(PlayerId, false)); // only a fresh press hauls it in
        RunTicks(world, 5);
        Assert.Equal(AnchorState.Down, ship.Anchor);
    }

    [Fact]
    public void RaisingAnchor_TakesTenSeconds_ThenTheShipSails()
    {
        var (world, ship) = CreateWorld(new Vector2(20, 64));
        ship.IsAnchored = true;
        world.Step();
        world.Enqueue(new AnchorKeyCommand(PlayerId, true));      // start hauling
        world.Enqueue(new AdjustThrottleCommand(PlayerId, 3));     // no setting sail until the anchor is up
        world.Step();
        var anchoredAt = ship.Position;

        RunTicks(world, Anchoring.RaiseTicks - 2);
        Assert.Equal(AnchorState.Raising, ship.Anchor);
        Assert.Equal(anchoredAt, ship.Position);

        world.Enqueue(new AnchorKeyCommand(PlayerId, true)); // mashing X mid-haul does nothing
        RunTicks(world, 2);
        Assert.Equal(AnchorState.Weighed, ship.Anchor);
        Assert.Equal(0, ship.Throttle);

        world.Enqueue(new AdjustThrottleCommand(PlayerId, 3));
        RunTicks(world, SimConstants.TickRate * 2);
        Assert.True(ship.Position.X > anchoredAt.X + 1f, "should be under way after the anchor is up");
    }

    [Fact]
    public void AnchoringNearAnIsland_PlundersItForGold()
    {
        var (world, ship) = CreateWorld(new Vector2(37, 30), Isle()); // 3 tiles off the west shore
        ship.IsAnchored = true;

        // The plunder starts on the first tick at anchor, so it pays out exactly DurationSeconds later.
        RunTicks(world, Plundering.DurationTicks - 1);
        Assert.Equal(0, Gold(world));
        Assert.True(Plundering.Progress(ship) > 0.9f);

        world.Step();
        Assert.Equal(Island.DefaultPlunderGold, Gold(world));
        Assert.Equal(10, Island.DefaultPlunderGold);
        Assert.True(world.PlunderCooldownTicks(world.Islands[0]) > Plundering.CooldownTicks - 5);
    }

    [Fact]
    public void PlunderedIsland_YieldsNothingUntilItsCooldownEnds_ThenPaysAgain()
    {
        var (world, ship) = CreateWorld(new Vector2(37, 30), Isle());
        ship.IsAnchored = true;
        RunTicks(world, Plundering.DurationTicks + 1);
        Assert.Equal(10, Gold(world));

        // Stay anchored: nothing during the cooldown...
        RunTicks(world, Plundering.CooldownTicks - 10);
        Assert.Equal(10, Gold(world));
        Assert.Equal(0f, Plundering.Progress(ship));

        // ...then it's ripe again and a fresh plunder runs to completion.
        RunTicks(world, 10 + Plundering.DurationTicks + 1);
        Assert.Equal(20, Gold(world));
    }

    [Fact]
    public void RaisingAnchor_AbandonsAPlunderInProgress()
    {
        var (world, ship) = CreateWorld(new Vector2(37, 30), Isle());
        ship.IsAnchored = true;
        RunTicks(world, Plundering.DurationTicks / 2);

        world.Enqueue(new AnchorKeyCommand(PlayerId, true));
        RunTicks(world, Plundering.DurationTicks);

        Assert.Equal(0, Gold(world));
        Assert.Equal(0f, Plundering.Progress(ship));
        Assert.Equal(0, world.PlunderCooldownTicks(world.Islands[0]));
    }

    [Fact]
    public void AnchoringOutOfRange_PlundersNothing()
    {
        var (world, ship) = CreateWorld(new Vector2(40f - Plundering.Range - 1.5f, 30), Isle());
        ship.IsAnchored = true;

        RunTicks(world, Plundering.DurationTicks * 2);

        Assert.Equal(0, Gold(world));
    }

    [Fact]
    public void Cooldowns_ArePerIsland()
    {
        // Two islands side by side with the ship anchored between them, in range of both.
        var (world, ship) = CreateWorld(new Vector2(51, 30), Isle(1, 40f), Isle(2, 54f));
        ship.IsAnchored = true;

        RunTicks(world, Plundering.DurationTicks * 2 + 4);

        Assert.Equal(20, Gold(world));
        Assert.All(world.Islands, island => Assert.True(world.PlunderCooldownTicks(island) > 0));
    }

    [Fact]
    public void Pirates_DoNotPlunder()
    {
        var world = new World(new Vector2(128, 128));
        world.AddIsland(Isle());
        var pirate = world.SpawnShip(new Vector2(37, 30), 0f, ShipStats.Sloop);
        pirate.IsAnchored = true;

        RunTicks(world, Plundering.DurationTicks * 2);

        Assert.Equal(0, world.PlunderCooldownTicks(world.Islands[0]));
    }

    [Fact]
    public void PlunderableFrom_MatchesWhereAnchoringWouldPay()
    {
        var (world, ship) = CreateWorld(new Vector2(37, 30), Isle());
        var island = world.Islands[0];

        Assert.Same(island, Plundering.PlunderableFrom(world, new Vector2(40f - Plundering.Range + 0.1f, 30)));
        Assert.Null(Plundering.PlunderableFrom(world, new Vector2(40f - Plundering.Range - 0.1f, 30)));

        ship.IsAnchored = true;
        RunTicks(world, Plundering.DurationTicks + 1);
        Assert.Null(Plundering.PlunderableFrom(world, ship.Position)); // on cooldown now
    }
}
