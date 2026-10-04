using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

public class DiscoveryTests
{
    private static World CreateWorld() => new(new Vector2(192, 192)) { Wind = Vector2.Zero };

    [Theory]
    [InlineData(0f, 0f, true)]
    [InlineData(28f, 0f, true)]      // the left/right edge of sight, 28.3 tiles across
    [InlineData(-29f, 0f, false)]    // just past it
    [InlineData(0f, 31.5f, true)]    // the top/bottom edge, 31.8 tiles up and down
    [InlineData(0f, -32.5f, false)]  // past it
    [InlineData(20f, 20f, true)]     // a diagonal corner, inside both
    [InlineData(20f, 33f, false)]
    public void Sight_MatchesTheMaxZoomView(float dx, float dy, bool visible)
    {
        Assert.Equal(visible, Discovery.SightContains(new Vector2(dx, dy)));
    }

    [Fact]
    public void Map_StartsUndiscovered_ThenFillsInAroundPlayerShips()
    {
        var world = CreateWorld();
        Assert.Equal(48 * 48, world.Discovery.CellCount);
        Assert.False(world.Discovery.IsDiscovered(Team.Players, new Vector2(96, 96)));

        world.SpawnShip(new Vector2(96, 96), 0f, ShipStats.Sloop, 1).IsAnchored = true;
        world.Step();

        Assert.True(world.Discovery.IsDiscovered(Team.Players, new Vector2(96, 96)));
        Assert.True(world.Discovery.IsDiscovered(Team.Players, new Vector2(116, 76)));   // across-screen edge
        Assert.False(world.Discovery.IsDiscovered(Team.Players, new Vector2(150, 150)));  // far off
        Assert.False(world.Discovery.IsDiscovered(Team.Players, new Vector2(10, 10)));
    }

    [Fact]
    public void SailingDiscoversMore_AndAnnouncesOnlyNewCells()
    {
        var world = CreateWorld();
        var ship = world.SpawnShip(new Vector2(40, 96), 0f, ShipStats.Sloop, 1);
        world.Step();
        var start = world.Discovery.DiscoveredCount(Team.Players);
        world.DrainEvents();

        world.Step(); // standing still: nothing new
        Assert.Empty(world.DrainEvents().OfType<AreaDiscovered>());

        ship.Throttle = 5;
        ship.Speed = ship.CruiseSpeed;
        var announced = new List<int>();
        for (var t = 0; t < SimConstants.TickRate * 10; t++)
        {
            world.Step();
            foreach (var e in world.DrainEvents().OfType<AreaDiscovered>())
                announced.AddRange(e.Cells);
        }

        Assert.True(world.Discovery.DiscoveredCount(Team.Players) > start);
        Assert.Equal(world.Discovery.DiscoveredCount(Team.Players) - start, announced.Count);
        Assert.Equal(announced.Count, announced.Distinct().Count());
    }

    [Fact]
    public void Discoveries_AreSharedByTheTeam()
    {
        var world = CreateWorld();
        world.SpawnShip(new Vector2(30, 30), 0f, ShipStats.Sloop, 1).IsAnchored = true;
        world.SpawnShip(new Vector2(160, 160), 0f, ShipStats.Sloop, 2).IsAnchored = true;

        world.Step();

        // One discovery state for the whole team: both corners are on everyone's map.
        Assert.True(world.Discovery.IsDiscovered(Team.Players, new Vector2(30, 30)));
        Assert.True(world.Discovery.IsDiscovered(Team.Players, new Vector2(160, 160)));
    }

    [Fact]
    public void Pirates_DiscoverNothing()
    {
        var world = CreateWorld();
        world.SpawnShip(new Vector2(96, 96), 0f, ShipStats.Sloop).IsAnchored = true;

        world.Step();

        Assert.Equal(0, world.Discovery.DiscoveredCount(Team.Players));
        Assert.Equal(0, world.Discovery.DiscoveredCount(Team.Pirates));
    }

    [Fact]
    public void Islands_CountAsDiscoveredOnceAnyPartIsSeen()
    {
        var world = CreateWorld();
        // An island straddling the right/left edge of sight from a ship at (96, 96).
        var island = new Island(1, new[] { new Vector2(114, 72), new Vector2(124, 72), new Vector2(124, 66), new Vector2(114, 66) });
        world.AddIsland(island);
        Assert.False(world.Discovery.IsDiscovered(Team.Players, island));

        world.SpawnShip(new Vector2(96, 96), 0f, ShipStats.Sloop, 1).IsAnchored = true;
        world.Step();

        Assert.True(world.Discovery.IsDiscovered(Team.Players, island));
        Assert.False(world.Discovery.IsDiscovered(Team.Players, new Vector2(124, 66))); // its far corner itself is still unseen
    }
}
