using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

/// <summary>Gold won together is split: kills between everyone who recently hit the ship, plunder between everyone nearby.</summary>
public class GoldShareTests
{
    private static int Gold(World world, int playerId) => world.Players.TryGetValue(playerId, out var p) ? p.Gold : 0;

    private static World CreateWorld() => new(new Vector2(128, 128)) { Wind = Vector2.Zero };

    [Fact]
    public void Runs_CanStartWithGold_ForPlaytesting()
    {
        var crew = new List<(int, string)> { (1, "ANNE"), (2, "MARY") };

        Assert.Equal(0, Gold(Runs.Create(seed: 1, crew), 1));
        var world = Runs.Create(seed: 1, crew, startingGold: 750);
        Assert.Equal(750, Gold(world, 1));
        Assert.Equal(750, Gold(world, 2));
    }

    [Fact]
    public void Kill_IsSplitWithRecentAssists_AndTheRemainderGoesToTheKiller()
    {
        var world = CreateWorld();
        var killer = world.SpawnShip(new Vector2(20, 20), 0f, ShipStats.Sloop, 1);
        world.SpawnShip(new Vector2(20, 30), 0f, ShipStats.Sloop, 2);
        world.SpawnShip(new Vector2(20, 40), 0f, ShipStats.Sloop, 3);
        var pirate = world.SpawnShip(new Vector2(60, 60), 0f, ShipStats.Sloop, abilities: Loadouts.Pirate);
        PirateLevels.Apply(pirate, 2); // 10 gold

        world.Step();
        pirate.RecordPlayerHit(2, world.Tick);
        pirate.RecordPlayerHit(3, world.Tick);
        pirate.Health = 0f;
        pirate.LastHitByShipId = killer.Id;
        world.Step();

        Assert.Equal(4, Gold(world, 1));
        Assert.Equal(3, Gold(world, 2));
        Assert.Equal(3, Gold(world, 3));
        Assert.Equal(1, world.Players[1].Kills);
        Assert.Equal(0, world.Players[2].Kills);
    }

    [Fact]
    public void OldHits_DontShareTheKill()
    {
        var world = CreateWorld();
        var killer = world.SpawnShip(new Vector2(20, 20), 0f, ShipStats.Sloop, 1);
        world.SpawnShip(new Vector2(20, 30), 0f, ShipStats.Sloop, 2);
        var pirate = world.SpawnShip(new Vector2(60, 60), 0f, ShipStats.Sloop, abilities: Loadouts.Pirate);

        pirate.RecordPlayerHit(2, world.Tick);
        for (var t = 0; t <= KillRewards.AssistTicks; t++)
            world.Step();
        pirate.Health = 0f;
        pirate.LastHitByShipId = killer.Id;
        world.Step();

        Assert.Equal(KillRewards.Gold, Gold(world, 1));
        Assert.Equal(0, Gold(world, 2));
    }

    [Fact]
    public void Hits_AreRememberedForTheirPlayer()
    {
        var world = CreateWorld();
        var shooter = world.SpawnShip(new Vector2(30, 30), 0f, ShipStats.Sloop, 1, Loadouts.Pirate);
        var pirate = world.SpawnShip(new Vector2(30, 34), 0f, ShipStats.Sloop, abilities: Loadouts.Pirate);

        world.Enqueue(new CastAbilityCommand(1, AbilitySlot.One, shooter.Position + new Vector2(0, 5)));
        for (var t = 0; t < SimConstants.TickRate; t++)
            world.Step();

        Assert.True(pirate.PlayerHits.ContainsKey(1));
    }

    [Fact]
    public void Plunder_IsSplitWithPlayersNearby_ButNotThoseFarOffOrSunk()
    {
        var world = CreateWorld();
        var island = new Island(7, new[] { new Vector2(40, 26), new Vector2(48, 26), new Vector2(48, 34), new Vector2(40, 34) });
        world.AddIsland(island);
        world.SpawnShip(new Vector2(37, 30), 0f, ShipStats.Sloop, 1).IsAnchored = true; // plundering
        world.SpawnShip(new Vector2(58, 30), 0f, ShipStats.Sloop, 2).IsAnchored = true; // 10 off the far shore
        world.SpawnShip(new Vector2(44, 70), 0f, ShipStats.Sloop, 3).IsAnchored = true; // well away
        var sunk = world.SpawnShip(new Vector2(44, 40), 0f, ShipStats.Sloop, 4);
        sunk.IsAnchored = true;
        sunk.Health = 0f;

        for (var t = 0; t <= Plundering.DurationTicks; t++)
            world.Step();

        var total = island.PlunderGold;
        Assert.Equal(total - total / 2, Gold(world, 1));
        Assert.Equal(total / 2, Gold(world, 2));
        Assert.Equal(0, Gold(world, 3));
        Assert.Equal(0, Gold(world, 4));
    }
}
