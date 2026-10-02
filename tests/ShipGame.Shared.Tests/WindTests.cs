using System.Numerics;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

public class WindTests
{
    private const int PlayerId = 1;

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    [Theory]
    [InlineData(0f, -1f, -1f)]   // north is screen-up: world (-1, -1)
    [InlineData(90f, 1f, -1f)]   // east is screen-right: world (1, -1)
    [InlineData(180f, 1f, 1f)]
    [InlineData(270f, -1f, 1f)]
    [InlineData(225f, 0f, 1.414f)] // south-west is world +Y
    public void Compass_MapsBearingsToScreenDirections(float bearing, float x, float y)
    {
        var expected = Vector2.Normalize(new Vector2(x, y));
        var actual = Compass.Direction(bearing);

        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(bearing % 360f, Compass.Bearing(actual), 2);
    }

    [Fact]
    public void World_DefaultsToASouthWesterly()
    {
        var world = new World(new Vector2(64, 64));

        Assert.Equal(Compass.SouthWest, Compass.Bearing(world.Wind), 2);
        Assert.Equal(World.DefaultWindSpeed, world.Wind.Length(), 4);
    }

    [Fact]
    public void ShipAtRest_DriftsDownwind()
    {
        var world = new World(new Vector2(64, 64));
        var ship = world.SpawnShip(new Vector2(32, 32), 0f, ShipStats.Sloop, PlayerId);

        RunTicks(world, SimConstants.TickRate * 10);

        var moved = ship.Position - new Vector2(32, 32);
        Assert.True(moved.Length() > 5f, $"only drifted {moved.Length()}");
        Assert.Equal(Compass.SouthWest, Compass.Bearing(moved), 0);
        Assert.Equal(0f, ship.Heading); // set sideways, not turned
    }

    [Fact]
    public void ShipUnderWay_HoldsCourseAgainstTheWind()
    {
        var world = new World(new Vector2(64, 64));
        var ship = world.SpawnShip(new Vector2(10, 32), 0f, ShipStats.Sloop, PlayerId);
        ship.Throttle = 3;
        ship.Speed = ship.CruiseSpeed;

        RunTicks(world, SimConstants.TickRate * 5);

        Assert.Equal(32f, ship.Position.Y, 3);
    }

    [Fact]
    public void Drift_BuildsUpGradually()
    {
        var world = new World(new Vector2(64, 64));
        var ship = world.SpawnShip(new Vector2(32, 32), 0f, ShipStats.Sloop, PlayerId);

        world.Step();
        Assert.True(ship.WindDrift.Length() < World.DefaultWindSpeed * 0.1f);

        RunTicks(world, SimConstants.TickRate * 6); // several response times: within a few percent of the wind
        Assert.InRange(ship.WindDrift.Length(), World.DefaultWindSpeed * 0.95f, World.DefaultWindSpeed);
    }

    [Fact]
    public void AnchoredShip_HoldsStation()
    {
        var world = new World(new Vector2(64, 64));
        var ship = world.SpawnShip(new Vector2(32, 32), 0f, ShipStats.Sloop);
        ship.IsAnchored = true;

        RunTicks(world, SimConstants.TickRate * 10);

        Assert.Equal(new Vector2(32, 32), ship.Position);
    }

    [Fact]
    public void MoveCommand_StillArrivesAgainstTheWind()
    {
        // Target dead upwind at the slowest sail: the glide-in fights the set the whole way.
        var world = new World(new Vector2(64, 64));
        var ship = world.SpawnShip(new Vector2(32, 32), 0f, ShipStats.Sloop, PlayerId);
        ship.Throttle = 1;
        var target = ship.Position - world.Wind / world.Wind.Length() * 6f;

        world.Enqueue(new MoveCommand(PlayerId, target));
        RunTicks(world, SimConstants.TickRate * 40);

        Assert.Null(ship.MoveTarget);
    }
}
