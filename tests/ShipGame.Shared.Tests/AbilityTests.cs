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
    [InlineData(-1f)] // cursor to port: heading +X in a Y-down world, so port is -Y
    [InlineData(1f)]  // cursor to starboard: +Y
    public void Broadside_FiresOutOfTheSideTheCursorIsOn(float expectedYSign)
    {
        var (world, ship) = CreateWorld();

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.One, ship.Position + new Vector2(2, 6 * expectedYSign)));
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

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.One, Vector2.Zero)); // (0,0) is off the port side
        world.Step();
        Assert.False(volley.IsChannelReady(BroadsideVolley.PortChannel));

        // Casts on one side are allowed exactly CooldownTicks ticks apart; one tick early is rejected.
        RunTicks(world, volley.Definition.CooldownTicks - 2);
        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.One, Vector2.Zero));
        world.Step();
        Assert.Empty(world.Projectiles); // the first volley has expired and the recast was rejected

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.One, Vector2.Zero));
        world.Step();
        Assert.Equal(BroadsideVolley.CannonCount, world.Projectiles.Count);
    }

    [Fact]
    public void Broadside_SidesReloadIndependently()
    {
        var (world, ship) = CreateWorld();
        var broadside = ship.GetAbility(AbilitySlot.One)!;
        var starboardAim = ship.Position + new Vector2(0, 5);
        var portAim = ship.Position + new Vector2(0, -5);

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.One, starboardAim));
        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.One, portAim));        // other deck: still loaded
        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.One, starboardAim));   // same deck again: reloading
        world.Step();

        Assert.Equal(2 * BroadsideVolley.CannonCount, world.Projectiles.Count);
        Assert.Contains(world.Projectiles, p => p.Velocity.Y > 0);
        Assert.Contains(world.Projectiles, p => p.Velocity.Y < 0);
        var rejected = Assert.Single(world.DrainEvents().OfType<CommandRejected>());
        Assert.Equal(RejectionReason.OnCooldown, rejected.Reason);

        Assert.False(broadside.IsReady); // both decks reloading
        Assert.Equal(broadside.Definition.CooldownTicks, broadside.RemainingTicks(BroadsideVolley.StarboardChannel));
        Assert.Equal(broadside.Definition.CooldownTicks, broadside.RemainingTicks(BroadsideVolley.PortChannel));
    }

    [Fact]
    public void Broadside_CastEventsNameTheDeck()
    {
        var (world, ship) = CreateWorld();
        world.DrainEvents();

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.One, ship.Position + new Vector2(0, 5)));
        world.Step();

        var cast = Assert.Single(world.DrainEvents().OfType<AbilityCast>());
        Assert.Equal(BroadsideVolley.StarboardChannel, cast.Channel);
    }

    [Fact]
    public void EmptySlots_DoNothing()
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        world.SpawnShip(new Vector2(30, 30), 0f, ShipStats.Sloop, PlayerId, Loadouts.Pirate); // pirate loadout: slots 2-4 empty

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

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.One, new Vector2(30, 40))); // starboard of a ship at (30,30) facing east
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

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.One, new Vector2(30, 40))); // starboard of a ship at (30,30) facing east
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

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.One, new Vector2(30, 40))); // starboard of a ship at (30,30) facing east
        RunTicks(world, SimConstants.TickRate);

        Assert.DoesNotContain(target, world.Ships);
    }

    [Fact]
    public void Covers_MatchesTheFiringLane()
    {
        var (_, ship) = CreateWorld();
        Assert.True(BroadsideVolley.Covers(ship, BroadsideSide.Starboard, ship.Position + new Vector2(0, 5), 1f));
        Assert.False(BroadsideVolley.Covers(ship, BroadsideSide.Starboard, ship.Position + new Vector2(0, -5), 1f)); // port side
        Assert.False(BroadsideVolley.Covers(ship, BroadsideSide.Starboard, ship.Position + new Vector2(5, 0), 1f));  // dead ahead
        Assert.False(BroadsideVolley.Covers(ship, BroadsideSide.Starboard, ship.Position + new Vector2(0, 15), 1f)); // out of range
        Assert.Equal(BroadsideSide.Port, BroadsideVolley.SideCovering(ship, ship.Position + new Vector2(0, -5), 1f));
        Assert.Equal(BroadsideSide.None, BroadsideVolley.SideCovering(ship, ship.Position + new Vector2(5, 0), 1f));
    }
}
