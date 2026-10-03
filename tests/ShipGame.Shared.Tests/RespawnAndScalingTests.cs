using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Tests;

public class RespawnAndScalingTests
{
    private static World CoopWorld(int players)
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        for (var id = 1; id <= players; id++)
            world.SpawnShip(new Vector2(80 + id * 6, 96), 0f, ShipStats.Sloop, id, Loadouts.Sloop);
        world.DrainEvents();
        return world;
    }

    private static void Sink(World world, int playerId)
    {
        world.GetPlayerShip(playerId)!.Health = 0f;
        world.Step();
    }

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    [Fact]
    public void SunkPlayer_RespawnsAfterTheDelay_NearATeammate()
    {
        var world = CoopWorld(players: 2);
        Sink(world, 1);

        Assert.Null(world.GetPlayerShip(1));
        Assert.True(world.Players[1].IsAwaitingRespawn);
        Assert.False(world.IsRunOver);

        RunTicks(world, Respawning.DelayTicks - 2);
        Assert.Null(world.GetPlayerShip(1));

        RunTicks(world, 2);
        var reborn = world.GetPlayerShip(1);
        Assert.NotNull(reborn);
        Assert.False(world.Players[1].IsAwaitingRespawn);
        Assert.Equal(reborn.Stats.MaxHealth, reborn.Health);
        Assert.InRange(Vector2.Distance(reborn.Position, world.GetPlayerShip(2)!.Position), 6f, 10f);
    }

    [Fact]
    public void Respawn_KeepsGoldUpgradesAndKillBonuses()
    {
        var world = CoopWorld(players: 2);
        var ship = world.GetPlayerShip(1)!;
        world.AddGold(1, 42);
        ship.AddModifier(UpgradeCatalog.Find("hull")!.Modifier);
        ship.AddModifier(UpgradeCatalog.Find("damage")!.Modifier);
        ship.AddModifier(new Stats.StatModifier(Stats.StatId.MaxSpeed, Stats.ModifierKind.Percent, KillRewards.SpeedBonus, KillRewards.Source));
        var statsBefore = ship.Stats;

        Sink(world, 1);
        RunTicks(world, Respawning.DelayTicks);

        var reborn = world.GetPlayerShip(1)!;
        Assert.Equal(42, world.Players[1].Gold);
        Assert.Equal(statsBefore, reborn.Stats);
        Assert.Equal(1, Shipyards.Level(reborn, UpgradeCatalog.Find("hull")!));
        Assert.Equal(statsBefore.MaxHealth, reborn.Health);
        Assert.NotNull(reborn.GetAbility(AbilitySlot.One)); // guns came back too
    }

    [Fact]
    public void Respawn_AvoidsLandAndPirates()
    {
        var world = new World(Archipelago.Size) { Wind = Vector2.Zero };
        foreach (var island in Archipelago.CreateIslands())
            world.AddIsland(island);
        world.SpawnShip(new Vector2(96, 96), 0f, ShipStats.Sloop, 1).IsAnchored = true;
        world.SpawnShip(new Vector2(100, 96), 0f, ShipStats.Sloop, 2).IsAnchored = true;
        world.SpawnShip(new Vector2(96, 104), 0f, ShipStats.Sloop).IsAnchored = true; // a pirate lurking on one side

        Sink(world, 1);
        RunTicks(world, Respawning.DelayTicks);

        var reborn = world.GetPlayerShip(1)!;
        Assert.True(world.DistanceToLand(reborn.Position) >= 3f);
        Assert.True(Vector2.Distance(reborn.Position, new Vector2(96, 104)) >= 10f);
    }

    [Fact]
    public void Wipe_EndsTheRun_AndNobodyRespawns()
    {
        var world = CoopWorld(players: 2);
        Sink(world, 1);
        var events = world.DrainEvents();
        Assert.Single(events.OfType<PlayerSunk>());

        Sink(world, 2); // last one standing goes down before the first is back

        Assert.True(world.IsRunOver);
        Assert.Single(world.DrainEvents().OfType<RunEnded>());
        RunTicks(world, Respawning.DelayTicks * 2);
        Assert.Null(world.GetPlayerShip(1));
        Assert.Null(world.GetPlayerShip(2));
    }

    [Fact]
    public void Solo_SinkingEndsTheRun()
    {
        var world = CoopWorld(players: 1);

        Sink(world, 1);

        Assert.True(world.IsRunOver);
        Assert.False(world.Players[1].IsAwaitingRespawn);
    }

    [Fact]
    public void RespawnEvents_AreAnnounced()
    {
        var world = CoopWorld(players: 3);
        Sink(world, 2);
        var sunk = Assert.Single(world.DrainEvents().OfType<PlayerSunk>());
        Assert.Equal((2, Respawning.DelayTicks), (sunk.PlayerId, sunk.RespawnTicks));

        RunTicks(world, Respawning.DelayTicks);

        var back = Assert.Single(world.DrainEvents().OfType<PlayerRespawned>());
        Assert.Equal(world.GetPlayerShip(2)!.Id, back.ShipId);
    }

    [Theory]
    [InlineData(1, 1, 2)]
    [InlineData(2, 1, 3)]
    [InlineData(4, 1, 5)]
    [InlineData(1, 7, 8)]
    [InlineData(2, 7, 12)]
    [InlineData(1, 50, 8)]
    [InlineData(12, 50, 40)] // 8 x 6.5 = 52, held at the absolute ceiling
    public void WaveSize_ScalesWithPlayers(int players, int wave, int expected)
    {
        Assert.Equal(expected, WaveDirector.WaveSize(wave, players));
    }

    [Fact]
    public void Waves_SpawnMorePiratesForMorePlayers()
    {
        var waves = new WaveDirector(5);
        var world = new World(new Vector2(192, 192)) { Waves = waves };
        for (var id = 1; id <= 3; id++)
            world.SpawnShip(new Vector2(80 + id * 10, 96), 0f, ShipStats.Sloop, id).IsAnchored = true;

        while (waves.Wave < 1)
            world.Step();

        Assert.Equal(WaveDirector.WaveSize(1, players: 3), world.Ships.Count(s => s.Team == Team.Pirates));
        Assert.Equal(4, WaveDirector.WaveSize(1, players: 3));
    }

    [Fact]
    public void Waves_StopOnceTheRunIsOver()
    {
        var waves = new WaveDirector(5);
        var world = new World(new Vector2(192, 192)) { Waves = waves };
        world.SpawnShip(new Vector2(96, 96), 0f, ShipStats.Sloop, 1);
        Sink(world, 1);

        RunTicks(world, (int)(WaveDirector.FirstWaveDelaySeconds * SimConstants.TickRate) * 3);

        Assert.Equal(0, waves.Wave);
        Assert.DoesNotContain(world.Ships, s => s.Team == Team.Pirates);
    }
}
