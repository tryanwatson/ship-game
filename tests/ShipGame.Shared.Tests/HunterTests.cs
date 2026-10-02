using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

public class HunterTests
{
    private const int PlayerId = 1;

    private static (Ship ship, HunterBehavior behavior) SpawnHunter(World world, Vector2 position, float heading, Vector2? home = null)
    {
        var hunter = world.SpawnShip(position, heading, ShipStats.Sloop, abilities: Loadouts.Sloop);
        var behavior = new HunterBehavior(home ?? position);
        hunter.Behavior = behavior;
        hunter.IsAnchored = true;
        return (hunter, behavior);
    }

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    [Fact]
    public void Guarding_IgnoresEnemiesOutsideAggroRange()
    {
        var world = new World(new Vector2(192, 192));
        var player = world.SpawnShip(new Vector2(100, 100), 0f, ShipStats.Sloop, PlayerId);
        player.IsAnchored = true;
        var (hunter, behavior) = SpawnHunter(world, new Vector2(100 + HunterBehavior.AggroRange + 2f, 100), MathF.PI);
        var home = hunter.Position;

        RunTicks(world, SimConstants.TickRate * 10);

        Assert.Equal(HunterState.Guarding, behavior.State);
        Assert.Equal(home, hunter.Position); // at anchor, not drifting
        Assert.Empty(world.Projectiles);
    }

    [Fact]
    public void Guarding_AggroesWhenAnEnemyComesInRange()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(100, 100), 0f, ShipStats.Sloop, PlayerId);
        var (hunter, behavior) = SpawnHunter(world, new Vector2(100 + HunterBehavior.AggroRange + 5f, 100), MathF.PI);

        world.Enqueue(new MoveCommand(PlayerId, new Vector2(115, 100))); // sail toward it
        for (var t = 0; t < SimConstants.TickRate * 10 && behavior.State == HunterState.Guarding; t++)
            world.Step();

        Assert.Equal(HunterState.Hunting, behavior.State);
        Assert.Same(player, behavior.Target);
        Assert.True(Vector2.Distance(hunter.Position, player.Position) <= HunterBehavior.AggroRange + 0.5f);
        Assert.False(hunter.IsAnchored);
    }

    [Fact]
    public void Hunter_FiresWhenTargetIsAbeam()
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        world.SpawnShip(new Vector2(32, 36), 0f, ShipStats.Sloop, PlayerId);
        var (hunter, _) = SpawnHunter(world, new Vector2(32, 32), 0f); // player 4 tiles off the starboard beam

        world.Step();

        Assert.Contains(world.Projectiles, p => p.OwnerShipId == hunter.Id);
        Assert.False(hunter.GetAbility(AbilitySlot.Two)!.IsReady); // starboard volley spent
        Assert.True(hunter.GetAbility(AbilitySlot.One)!.IsReady);  // port held
    }

    [Fact]
    public void Hunter_SinksAPassiveTargetInRange()
    {
        var world = new World(new Vector2(64, 64));
        var player = world.SpawnShip(new Vector2(32, 32), 0f, ShipStats.Sloop, PlayerId);
        SpawnHunter(world, new Vector2(45, 22), MathF.PI);

        for (var t = 0; t < SimConstants.TickRate * 90 && world.Ships.Contains(player); t++)
            world.Step();

        Assert.DoesNotContain(player, world.Ships);
    }

    [Fact]
    public void Leash_TargetEscapes_HunterReturnsHomeAndGuards()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(100, 100), 0f, ShipStats.Sloop, PlayerId);
        var (hunter, behavior) = SpawnHunter(world, new Vector2(110, 100), MathF.PI);
        world.Step();
        Assert.Equal(HunterState.Hunting, behavior.State);

        player.Position = new Vector2(20, 20); // out of reach
        world.Step();
        Assert.Equal(HunterState.Returning, behavior.State);

        for (var t = 0; t < SimConstants.TickRate * 30 && behavior.State != HunterState.Guarding; t++)
            world.Step();

        Assert.Equal(HunterState.Guarding, behavior.State);
        Assert.True(Vector2.Distance(hunter.Position, behavior.Home) <= ShipMovement.ArriveRadius + 0.5f);
        Assert.True(hunter.IsAnchored);
    }

    [Fact]
    public void Leash_DraggedTooFarFromHome_HunterTurnsBack()
    {
        // The player runs straight away at full sail: the pirate keeps pace (so the target never escapes),
        // until the chase takes it past its leash.
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(100, 100), 0f, ShipStats.Sloop, PlayerId);
        player.Throttle = ShipMovement.ThrottleLevels;
        player.Speed = player.CruiseSpeed;
        var (hunter, behavior) = SpawnHunter(world, new Vector2(88, 100), 0f);

        var maxFromHome = 0f;
        for (var t = 0; t < SimConstants.TickRate * 30 && behavior.State != HunterState.Returning; t++)
        {
            world.Step();
            maxFromHome = MathF.Max(maxFromHome, Vector2.Distance(hunter.Position, behavior.Home));
            Assert.True(Vector2.Distance(hunter.Position, player.Position) <= HunterBehavior.DisengageRange,
                "target escaped; this test is about the home leash");
        }

        Assert.Equal(HunterState.Returning, behavior.State);
        Assert.InRange(maxFromHome, HunterBehavior.LeashRange, HunterBehavior.LeashRange + 1f);
    }

    [Fact]
    public void Returning_IgnoresEnemiesUntilHome()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(100, 100), 0f, ShipStats.Sloop, PlayerId);
        player.IsAnchored = true;
        var (hunter, behavior) = SpawnHunter(world, new Vector2(110, 100), MathF.PI, home: new Vector2(125, 100));
        world.Step();
        player.Position = new Vector2(20, 20);
        world.Step();
        Assert.Equal(HunterState.Returning, behavior.State);

        player.Position = hunter.Position + new Vector2(0, 5); // right alongside, mid-return
        RunTicks(world, SimConstants.TickRate);

        Assert.Equal(HunterState.Returning, behavior.State);
        Assert.Empty(world.Projectiles);
    }

    [Fact]
    public void Hunter_ReturnsHomeWhenItsTargetIsSunk()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(100, 100), 0f, ShipStats.Sloop, PlayerId);
        var (_, behavior) = SpawnHunter(world, new Vector2(110, 100), MathF.PI, home: new Vector2(125, 100));
        world.Step();

        player.Health = 0f;
        RunTicks(world, 2);
        Assert.Equal(HunterState.Returning, behavior.State);

        RunTicks(world, SimConstants.TickRate * 15);
        Assert.Equal(HunterState.Guarding, behavior.State);
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
