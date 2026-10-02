using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

public class AbilityTests
{
    private const int PlayerId = 1;

    private static (World world, Ship ship) CreateWorld()
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        var ship = world.SpawnShip(new Vector2(30, 30), 0f, ShipStats.Sloop, PlayerId, Loadouts.Sloop);
        return (world, ship);
    }

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    [Theory]
    [InlineData(AbilitySlot.One, -1f)] // port: heading +X in a Y-down world, so port is -Y
    [InlineData(AbilitySlot.Two, 1f)]  // starboard: +Y
    public void Volley_FiresOutOfTheCorrectSide(AbilitySlot slot, float expectedYSign)
    {
        var (world, ship) = CreateWorld();

        world.Enqueue(new CastAbilityCommand(PlayerId, slot, Vector2.Zero));
        world.Step();

        Assert.Equal(BroadsideVolley.CannonCount, world.Projectiles.Count);
        Assert.All(world.Projectiles, p =>
        {
            Assert.Equal(expectedYSign, MathF.Sign(p.Velocity.Y));
            Assert.Equal(expectedYSign, MathF.Sign(p.Position.Y - ship.Position.Y));
        });
    }

    [Fact]
    public void Cooldown_BlocksRecastUntilItExpires()
    {
        var (world, ship) = CreateWorld();
        var volley = ship.GetAbility(AbilitySlot.One)!;

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.One, Vector2.Zero));
        world.Step();
        Assert.False(volley.IsReady);

        // Casts are allowed exactly CooldownTicks ticks apart; one tick early is rejected.
        RunTicks(world, volley.Definition.CooldownTicks - 2);
        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.One, Vector2.Zero));
        world.Step();
        Assert.Empty(world.Projectiles); // the first volley has expired and the recast was rejected

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.One, Vector2.Zero));
        world.Step();
        Assert.Equal(BroadsideVolley.CannonCount, world.Projectiles.Count);
    }

    [Fact]
    public void EmptySlots_DoNothing()
    {
        var (world, _) = CreateWorld();

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.Three, Vector2.Zero));
        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.Four, Vector2.Zero));
        world.Enqueue(new CastAbilityCommand(PlayerId, (AbilitySlot)99, Vector2.Zero));
        world.Step();

        Assert.Empty(world.Projectiles);
    }

    [Fact]
    public void Volley_DamagesShipAbeam_ButNotTheCaster()
    {
        var (world, ship) = CreateWorld();
        var target = world.SpawnShip(new Vector2(30, 35), 0f, ShipStats.Sloop);

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.Two, Vector2.Zero));
        RunTicks(world, SimConstants.TickRate);

        Assert.True(target.Health < target.Stats.MaxHealth);
        Assert.Equal(ship.Stats.MaxHealth, ship.Health);
        Assert.Empty(world.Projectiles);
    }

    [Fact]
    public void Volley_MissesShipOnTheOtherSide()
    {
        var (world, _) = CreateWorld();
        var target = world.SpawnShip(new Vector2(30, 35), 0f, ShipStats.Sloop);

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.One, Vector2.Zero));
        RunTicks(world, SimConstants.TickRate);

        Assert.Equal(target.Stats.MaxHealth, target.Health);
    }

    [Fact]
    public void Projectiles_ExpireAfterRange()
    {
        var (world, ship) = CreateWorld();

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.Two, Vector2.Zero));
        world.Step();
        var maxTravel = 0f;
        while (world.Projectiles.Count > 0)
        {
            maxTravel = world.Projectiles.Max(p => p.Position.Y - ship.Position.Y);
            world.Step();
        }

        Assert.InRange(maxTravel, BroadsideVolley.Range * 0.9f, BroadsideVolley.Range + ship.Stats.Beam + 0.5f);
    }

    [Fact]
    public void Ship_SinksAtZeroHealth()
    {
        var (world, _) = CreateWorld();
        var target = world.SpawnShip(new Vector2(30, 35), 0f, ShipStats.Sloop);
        target.Health = BroadsideVolley.Damage;

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.Two, Vector2.Zero));
        RunTicks(world, SimConstants.TickRate);

        Assert.DoesNotContain(target, world.Ships);
    }

    [Fact]
    public void Covers_MatchesTheFiringLane()
    {
        var (_, ship) = CreateWorld();
        var starboard = (BroadsideVolley)ship.GetAbility(AbilitySlot.Two)!.Definition;

        Assert.True(starboard.Covers(ship, ship.Position + new Vector2(0, 5), 1f));
        Assert.False(starboard.Covers(ship, ship.Position + new Vector2(0, -5), 1f)); // port side
        Assert.False(starboard.Covers(ship, ship.Position + new Vector2(5, 0), 1f));  // dead ahead
        Assert.False(starboard.Covers(ship, ship.Position + new Vector2(0, 15), 1f)); // out of range
    }
}
