using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

/// <summary>A pirate's long gun and broadside show where they're laid before they fire, so they can be dodged.</summary>
public class ShotWarningTests
{
    private const int PlayerId = 1;

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    [Fact]
    public void APiratesLongGun_IsLaidFirst_AndFiresAfterItsWindup()
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        var pirate = world.SpawnShip(new Vector2(20, 20), 0f, ShipStats.PirateSloop, abilities: Loadouts.Starting(new LongGun()));
        var aim = pirate.Position + new Vector2(10, 0);
        world.DrainEvents();

        Assert.True(world.TryCastAbility(pirate, AbilitySlot.One, aim));

        Assert.Empty(world.Projectiles);
        var warning = Assert.Single(world.Warnings);
        Assert.Equal(aim, warning.Target);
        Assert.Equal(world.Tick + LongGun.PirateWindupTicks, warning.FireTick);
        Assert.Contains(world.DrainEvents(), e => e is ShotWarned w && w.ShipId == pirate.Id && w.Target == aim);
        Assert.False(world.TryCastAbility(pirate, AbilitySlot.One, aim)); // reloading already: it can't be laid twice

        RunTicks(world, LongGun.PirateWindupTicks);
        Assert.Empty(world.Projectiles); // laid at this tick, it goes off in the step at FireTick
        RunTicks(world, 1);
        Assert.Empty(world.Warnings);
        var shot = Assert.Single(world.Projectiles);
        Assert.True(Vector2.Dot(Vector2.Normalize(shot.Velocity), Vector2.UnitX) > 0.999f);
    }

    [Fact]
    public void APlayersLongGun_FiresAtOnce()
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        var ship = world.SpawnShip(new Vector2(20, 20), 0f, ShipStats.Sloop, PlayerId, Loadouts.Starting(new LongGun()));

        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + new Vector2(10, 0));

        Assert.Empty(world.Warnings);
        Assert.Single(world.Projectiles);
    }

    [Fact]
    public void AShotLaidByAShipThatSinks_IsNeverFired()
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        var pirate = world.SpawnShip(new Vector2(20, 20), 0f, ShipStats.PirateSloop, abilities: Loadouts.Starting(new LongGun()));
        world.TryCastAbility(pirate, AbilitySlot.One, pirate.Position + new Vector2(10, 0));

        pirate.Health = 0f;
        RunTicks(world, LongGun.PirateWindupTicks + 1);

        Assert.Empty(world.Warnings);
        Assert.Empty(world.Projectiles);
    }

    [Fact]
    public void APiratesBroadside_FiresTheSideItLitUp_EvenAfterTurning()
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        var pirate = world.SpawnShip(new Vector2(32, 32), 0f, ShipStats.PirateSloop, abilities: Loadouts.Pirate);
        world.DrainEvents();

        world.TryCastAbility(pirate, AbilitySlot.One, pirate.Position + new Vector2(0, 5)); // starboard (Y-down)
        Assert.Empty(world.Projectiles);
        Assert.Equal(BroadsideVolley.StarboardChannel, Assert.Single(world.Warnings).Channel);
        Assert.Contains(world.DrainEvents(), e => e is ShotWarned { Channel: BroadsideVolley.StarboardChannel });

        pirate.Heading = MathF.PI; // swung right round: the old aim point is off the port side now
        for (var t = 0; t <= BroadsideVolley.PirateWindupTicks; t++)
            world.Step();

        Assert.NotEmpty(world.Projectiles);
        var starboard = BroadsideVolley.FiringDirection(pirate, BroadsideSide.Starboard);
        Assert.All(world.Projectiles, p => Assert.True(Vector2.Dot(Vector2.Normalize(p.Velocity), starboard) > 0.9f));
        Assert.Contains(world.DrainEvents(), e => e is AbilityCast { Channel: BroadsideVolley.StarboardChannel });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASniper_HitsAShipHoldingItsCourse_ButOneThatTurnsAway_Escapes(bool turnOnWarning)
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(60, 100), 0f, ShipStats.Sloop, PlayerId);
        player.Throttle = ShipMovement.ThrottleLevels; // sailing east
        player.Speed = player.Stats.MaxSpeed;
        var sniper = world.SpawnShip(new Vector2(60, 86), 0f, ShipStats.PirateSloop, abilities: Loadouts.Starting(new LongGun()));
        sniper.Behavior = new HunterBehavior(sniper.Position);
        sniper.IsAnchored = true;

        // Watch the first shot only.
        long? firstFire = null;
        for (var t = 0; t < SimConstants.TickRate * 4 && (firstFire is null || world.Tick <= firstFire + SimConstants.TickRate); t++)
        {
            world.Step();
            if (firstFire is null && world.Warnings.Count > 0)
            {
                firstFire = world.Warnings[0].FireTick;
                if (turnOnWarning)
                    player.Rudder = 1; // hard to starboard, away from the gun
            }
        }

        Assert.NotNull(firstFire);
        if (turnOnWarning)
            Assert.Equal(player.Stats.MaxHealth, player.Health);
        else
            Assert.Equal(sniper.Id, player.LastHitByShipId);
    }
}
