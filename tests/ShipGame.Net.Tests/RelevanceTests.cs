using System.Numerics;
using ShipGame.Net;
using ShipGame.Shared.Simulation;

namespace ShipGame.Net.Tests;

public class RelevanceTests
{
    private static (World World, Relevance Relevance, List<Ship> Entered, List<int> Left) Create() =>
        (new World(new Vector2(96, 900)), new Relevance(), new List<Ship>(), new List<int>());

    [Fact]
    public void PlayerShips_AreAlwaysShown_PiratesOnlyNearAPlayer()
    {
        var (world, relevance, entered, left) = Create();
        var player = world.SpawnShip(new Vector2(48, 880), 0f, ShipStats.Sloop, 1);
        var far = world.SpawnShip(new Vector2(48, 100), 0f, ShipStats.Sloop, 2); // a crewmate far up the map
        var near = world.SpawnShip(new Vector2(48, 840), 0f, ShipStats.Sloop);
        var distant = world.SpawnShip(new Vector2(48, 500), 0f, ShipStats.Sloop);
        var besideCrewmate = world.SpawnShip(new Vector2(60, 110), 0f, ShipStats.Sloop);

        relevance.Update(world, entered, left);

        Assert.Equal(new[] { player, far, near, besideCrewmate }, entered);
        Assert.Empty(left);
        Assert.False(relevance.IsShown(distant.Id));
    }

    [Fact]
    public void ShownShips_StayShownUntilWellOutOfRange()
    {
        var (world, relevance, entered, left) = Create();
        world.SpawnShip(new Vector2(48, 880), 0f, ShipStats.Sloop, 1);
        var pirate = world.SpawnShip(new Vector2(48, 880 - Relevance.EnterRange - 1f), 0f, ShipStats.Sloop);

        relevance.Update(world, entered, left);
        Assert.False(relevance.IsShown(pirate.Id));

        pirate.Position = new Vector2(48, 880 - Relevance.EnterRange + 1f);
        relevance.Update(world, entered, left);
        Assert.Contains(pirate, entered);

        // Drifting just past where it came in: still shown, so it doesn't flicker.
        entered.Clear();
        pirate.Position = new Vector2(48, 880 - Relevance.LeaveRange + 1f);
        relevance.Update(world, entered, left);
        Assert.True(relevance.IsShown(pirate.Id));
        Assert.Empty(entered);
        Assert.Empty(left);

        pirate.Position = new Vector2(48, 880 - Relevance.LeaveRange - 1f);
        relevance.Update(world, entered, left);
        Assert.Equal(new[] { pirate.Id }, left);
        Assert.False(relevance.IsShown(pirate.Id));
    }

    [Fact]
    public void SunkShips_AreForgotten_WithoutBeingReported()
    {
        var (world, relevance, entered, left) = Create();
        world.SpawnShip(new Vector2(48, 880), 0f, ShipStats.Sloop, 1);
        var pirate = world.SpawnShip(new Vector2(48, 870), 0f, ShipStats.Sloop);
        relevance.Update(world, entered, left);

        world.RemoveShip(pirate.Id);
        relevance.Update(world, entered, left);

        Assert.Empty(left);
        Assert.False(relevance.IsShown(pirate.Id));
    }
}
