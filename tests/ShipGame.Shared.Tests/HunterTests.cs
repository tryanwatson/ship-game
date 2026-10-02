using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

public class HunterTests
{
    private const int PlayerId = 1;

    private static Ship SpawnHunter(World world, Vector2 position, float heading)
    {
        var hunter = world.SpawnShip(position, heading, ShipStats.Sloop, abilities: Loadouts.Sloop);
        hunter.Behavior = new HunterBehavior();
        return hunter;
    }

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    [Fact]
    public void Hunter_FiresWhenTargetIsAbeam()
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(32, 36), 0f, ShipStats.Sloop, PlayerId);
        var hunter = SpawnHunter(world, new Vector2(32, 32), 0f); // player 4 tiles off the starboard beam

        world.Step();

        Assert.Contains(world.Projectiles, p => p.OwnerShipId == hunter.Id);
        Assert.False(hunter.GetAbility(AbilitySlot.Two)!.IsReady); // starboard volley spent
        Assert.True(hunter.GetAbility(AbilitySlot.One)!.IsReady);  // port held
    }

    [Fact]
    public void Hunter_ChasesDownADistantTarget()
    {
        var world = new World(new Vector2(64, 64));
        var player = world.SpawnShip(new Vector2(50, 50), 0f, ShipStats.Sloop, PlayerId);
        player.IsAnchored = true;
        var hunter = SpawnHunter(world, new Vector2(8, 8), MathF.PI); // facing away, 59 tiles off

        RunTicks(world, SimConstants.TickRate * 20);

        Assert.True(Vector2.Distance(hunter.Position, player.Position) < 12f);
    }

    [Fact]
    public void Hunter_SinksAPassiveTarget()
    {
        var world = new World(new Vector2(64, 64));
        var player = world.SpawnShip(new Vector2(32, 32), 0f, ShipStats.Sloop, PlayerId);
        SpawnHunter(world, new Vector2(45, 20), MathF.PI);

        for (var t = 0; t < SimConstants.TickRate * 90 && world.Ships.Contains(player); t++)
            world.Step();

        Assert.DoesNotContain(player, world.Ships);
    }

    [Fact]
    public void Hunter_HeavesToWhenNothingIsLeftToHunt()
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        var hunter = SpawnHunter(world, new Vector2(32, 32), 0f);
        hunter.Throttle = 5;
        hunter.Speed = hunter.CruiseSpeed;

        RunTicks(world, SimConstants.TickRate * 6);

        Assert.Equal(0, hunter.Throttle);
        Assert.Equal(0f, hunter.Speed);
        Assert.Empty(world.Projectiles);
    }

    [Fact]
    public void Volleys_DoNotHitFriendlyShips()
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        var shooter = world.SpawnShip(new Vector2(30, 30), 0f, ShipStats.Sloop, abilities: Loadouts.Sloop);
        var friend = world.SpawnShip(new Vector2(30, 34), 0f, ShipStats.Sloop);

        world.TryCastAbility(shooter, AbilitySlot.Two, Vector2.Zero);
        RunTicks(world, SimConstants.TickRate);

        Assert.Equal(Team.Pirates, friend.Team);
        Assert.Equal(friend.Stats.MaxHealth, friend.Health);
    }
}
