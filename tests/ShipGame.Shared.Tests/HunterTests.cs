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
        var hunter = world.SpawnShip(position, heading, ShipStats.Sloop, abilities: Loadouts.FullArsenal);
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

        Assert.Equal(HunterState.Patrolling, behavior.State);
        Assert.Equal(home, hunter.Position); // at anchor, not drifting
        Assert.Empty(world.Projectiles);
    }

    [Fact]
    public void Guarding_AggroesOnWhoeverHitsIt_EvenFromBeyondItsSight()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(100, 100), 0f, ShipStats.Sloop, PlayerId, Loadouts.FullArsenal);
        player.IsAnchored = true;
        var distance = Mortar.Range - 2f; // well past aggro, and past the range at which a chase is given up
        var (hunter, behavior) = SpawnHunter(world, new Vector2(100 + distance, 100), MathF.PI);
        Assert.True(distance > HunterBehavior.DisengageRange - 5f);

        Assert.True(world.TryCastAbility(player, AbilitySlot.Three, hunter.Position));
        var strike = Assert.Single(world.Strikes);
        RunTicks(world, (int)(strike.ImpactTick - world.Tick) + 2);

        Assert.True(hunter.Health < hunter.Stats.MaxHealth, "the shell should have landed");
        Assert.Equal(HunterState.Hunting, behavior.State);
        Assert.Same(player, behavior.Target);
        Assert.False(hunter.IsAnchored);

        // Provoked, it doesn't give up for distance; it closes in.
        RunTicks(world, SimConstants.TickRate * 3);
        Assert.Equal(HunterState.Hunting, behavior.State);
        Assert.True(Vector2.Distance(hunter.Position, player.Position) < distance);
    }

    [Fact]
    public void Hits_FromItsOwnSide_DontProvokeIt()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var (hunter, behavior) = SpawnHunter(world, new Vector2(100, 100), 0f);
        var other = world.SpawnShip(new Vector2(130, 100), 0f, ShipStats.Sloop);
        hunter.LastHitByShipId = other.Id;
        hunter.LastHitTick = world.Tick;

        RunTicks(world, 2);

        Assert.Equal(HunterState.Patrolling, behavior.State);
    }

    [Fact]
    public void Guarding_AggroesWhenAnEnemyComesInRange()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(100, 100), 0f, ShipStats.Sloop, PlayerId);
        var (hunter, behavior) = SpawnHunter(world, new Vector2(100 + HunterBehavior.AggroRange + 5f, 100), MathF.PI);

        world.Enqueue(new MoveCommand(PlayerId, new Vector2(115, 100))); // sail toward it
        for (var t = 0; t < SimConstants.TickRate * 10 && behavior.State == HunterState.Patrolling; t++)
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
        var broadside = hunter.GetAbility(AbilitySlot.One)!;
        Assert.False(broadside.IsChannelReady(BroadsideVolley.StarboardChannel)); // starboard deck spent...
        Assert.True(broadside.IsChannelReady(BroadsideVolley.PortChannel));       // port still loaded
        Assert.All(world.Projectiles, p => Assert.True(p.Velocity.Y > 0f)); // ...out of the starboard side, toward the player
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

        for (var t = 0; t < SimConstants.TickRate * 30 && behavior.State != HunterState.Patrolling; t++)
            world.Step();

        Assert.Equal(HunterState.Patrolling, behavior.State);
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
        Assert.Equal(HunterState.Patrolling, behavior.State);
    }

    [Fact]
    public void Volleys_DoNotHitFriendlyShips()
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        var shooter = world.SpawnShip(new Vector2(30, 30), 0f, ShipStats.Sloop, abilities: Loadouts.FullArsenal);
        var friend = world.SpawnShip(new Vector2(30, 34), 0f, ShipStats.Sloop);

        world.TryCastAbility(shooter, AbilitySlot.One, shooter.Position + new Vector2(0, 5));
        RunTicks(world, SimConstants.TickRate);

        Assert.Equal(Team.Pirates, friend.Team);
        Assert.Equal(friend.Stats.MaxHealth, friend.Health);
    }

    // A 6x14 wall of land (84 sq tiles) spanning x 97..103, y 93..107.
    private static Island Wall() => new(1, new[] { new Vector2(97, 93), new Vector2(103, 93), new Vector2(103, 107), new Vector2(97, 107) });

    private static bool Touching(World world, Ship ship)
    {
        Span<Vector2> hull = stackalloc Vector2[HullShape.PointCount];
        HullShape.GetWorldOutline(ship.Position, ship.Heading, ship.Stats, hull);
        foreach (var point in hull)
        {
            if (world.DistanceToLand(point) < 0.05f)
                return true;
        }
        return false;
    }

    [Fact]
    public void Hunter_SailsAroundAnIslandToReachItsTarget()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        world.AddIsland(Wall());
        var player = world.SpawnShip(new Vector2(110, 100), 0f, ShipStats.Sloop, PlayerId); // behind the wall
        player.IsAnchored = true;
        player.Health = 1e6f;
        var (hunter, behavior) = SpawnHunter(world, new Vector2(91, 100), 0f); // bow pointed straight at it

        var closest = float.MaxValue;
        for (var t = 0; t < SimConstants.TickRate * 20; t++)
        {
            world.Step();
            Assert.False(Touching(world, hunter), $"tick {t}: ran onto the island at {hunter.Position}");
            closest = MathF.Min(closest, Vector2.Distance(hunter.Position, player.Position));
        }

        Assert.Equal(hunter.Stats.MaxHealth, hunter.Health);
        Assert.True(closest < 8f, $"never got closer than {closest}");
        Assert.Equal(HunterState.Hunting, behavior.State);
    }

    [Fact]
    public void Hunter_SailsHomeAroundAnIsland()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        world.AddIsland(Wall());
        var player = world.SpawnShip(new Vector2(85, 100), 0f, ShipStats.Sloop, PlayerId);
        player.IsAnchored = true;
        // Home is on the far side of the wall from where the pirate finds itself.
        var (hunter, behavior) = SpawnHunter(world, new Vector2(90, 100), 0f, home: new Vector2(110, 100));
        world.Step();
        Assert.Equal(HunterState.Hunting, behavior.State);
        player.Position = new Vector2(20, 20); // target gets away: leash
        var guarding = false;

        for (var t = 0; t < SimConstants.TickRate * 30 && !guarding; t++)
        {
            world.Step();
            Assert.False(Touching(world, hunter), $"tick {t}: ran onto the island at {hunter.Position}");
            guarding = behavior.State == HunterState.Patrolling;
        }

        Assert.True(guarding, $"never made it home; at {hunter.Position}, state {behavior.State}");
        Assert.True(Vector2.Distance(hunter.Position, behavior.Home) < 4f);
    }

    [Fact]
    public void Hunter_HoldsFireWhenLandIsInTheWay()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        world.AddIsland(new Island(1, new[] { new Vector2(98, 102), new Vector2(102, 102), new Vector2(102, 103), new Vector2(98, 103) }));
        world.SpawnShip(new Vector2(100, 106), 0f, ShipStats.Sloop, PlayerId); // abeam, but behind a strip of land
        SpawnHunter(world, new Vector2(100, 99), 0f);

        world.Step();

        Assert.Empty(world.Projectiles);
    }
}
