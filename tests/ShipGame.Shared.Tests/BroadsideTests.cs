using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

public class BroadsideTests
{
    private static Ship ShipAt(Vector2 position, float heading) =>
        new(1, null, ShipStats.Sloop) { Position = position, Heading = heading };

    [Theory]
    [InlineData(0f, 5f, BroadsideSide.Starboard)] // heading +X, target +Y (Y-down: to the right)
    [InlineData(0f, -5f, BroadsideSide.Port)]
    [InlineData(5f, 0f, BroadsideSide.None)] // dead ahead
    [InlineData(-5f, 0f, BroadsideSide.None)] // dead astern
    [InlineData(2f, 5f, BroadsideSide.Starboard)] // ~22 degrees forward of abeam, inside the arc
    [InlineData(5f, 3f, BroadsideSide.None)] // ~59 degrees off the bow, outside the arc
    public void GetSide_HeadingEast(float dx, float dy, BroadsideSide expected)
    {
        var ship = ShipAt(new Vector2(10, 10), heading: 0f);

        Assert.Equal(expected, Broadside.GetSide(ship, ship.Position + new Vector2(dx, dy)));
    }

    [Fact]
    public void GetSide_FollowsShipHeading()
    {
        // Heading +Y (south in a Y-down world): starboard is -X.
        var ship = ShipAt(new Vector2(10, 10), heading: MathF.PI / 2f);

        Assert.Equal(BroadsideSide.Starboard, Broadside.GetSide(ship, new Vector2(5, 10)));
        Assert.Equal(BroadsideSide.Port, Broadside.GetSide(ship, new Vector2(15, 10)));
    }
    // Heading +X (Y-down), so the starboard beam is +Y; degrees fore of it lean toward +X.
    private static Vector2 ForeOfStarboardBeam(float degrees, float distance)
    {
        var angle = (90f - degrees) * MathF.PI / 180f;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
    }

    private static float DegreesForeOfStarboardBeam(Vector2 velocity) =>
        90f - MathF.Atan2(velocity.Y, velocity.X) * 180f / MathF.PI;

    private static (World world, Ship ship) Gunner()
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        var ship = world.SpawnShip(new Vector2(32, 32), 0f, ShipStats.Sloop, 1, new Ability?[] { new BroadsideVolley(), null, null, null });
        return (world, ship);
    }

    [Theory]
    [InlineData(10f, 10f)]   // inside the window: laid right on it
    [InlineData(-12f, -12f)] // aft works too
    [InlineData(30f, 30f)]   // a player's window is wide
    [InlineData(50f, 35f)]   // well forward: held at the edge of the window
    [InlineData(-80f, -35f)]
    public void Broadside_IsLaidTowardTheCursor_WithinItsArc(float cursorDegrees, float expectedDegrees)
    {
        var (world, ship) = Gunner();

        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + ForeOfStarboardBeam(cursorDegrees, 6f));

        Assert.Equal(BroadsideVolley.CannonCount, world.Projectiles.Count);
        Assert.All(world.Projectiles, p => Assert.Equal(expectedDegrees, DegreesForeOfStarboardBeam(p.Velocity), 1));
    }

    [Fact]
    public void SwivelGuns_WidenTheArc()
    {
        var (world, ship) = Gunner();
        ship.AddCard(new ShipGame.Shared.Upgrades.CardPick("swivel-guns", 7));
        var arc = BroadsideVolley.AimArcFor(ship) * 180f / MathF.PI;
        Assert.True(arc > BroadsideVolley.AimArcDegrees + 10f, $"arc {arc}");

        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + ForeOfStarboardBeam(arc - 2f, 6f));

        Assert.All(world.Projectiles, p => Assert.Equal(arc - 2f, DegreesForeOfStarboardBeam(p.Velocity), 1));
    }

    [Fact]
    public void Covers_ReachesAsFarRoundAsTheGunsCanBeLaid()
    {
        var (_, ship) = Gunner();
        var reach = BroadsideVolley.RangeFor(ship) - 0.5f;

        Assert.True(BroadsideVolley.Covers(ship, BroadsideSide.Starboard, ship.Position + ForeOfStarboardBeam(14f, reach), 0.2f));
        Assert.True(BroadsideVolley.Covers(ship, BroadsideSide.Starboard, ship.Position + ForeOfStarboardBeam(-14f, reach), 0.2f));
        Assert.True(BroadsideVolley.Covers(ship, BroadsideSide.Starboard, ship.Position + ForeOfStarboardBeam(34f, reach), 0.2f));
        Assert.False(BroadsideVolley.Covers(ship, BroadsideSide.Starboard, ship.Position + ForeOfStarboardBeam(60f, reach), 0.2f));
        Assert.False(BroadsideVolley.Covers(ship, BroadsideSide.Starboard, ship.Position + ForeOfStarboardBeam(0f, reach + 2f), 0.2f));
    }

    [Fact]
    public void APiratesBroadside_KeepsItsNarrowWindow()
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        var pirate = world.SpawnShip(new Vector2(32, 32), 0f, ShipStats.PirateSloop, null, new Ability?[] { new BroadsideVolley(), null, null, null });

        Assert.Equal(BroadsideVolley.AimArcDegrees, BroadsideVolley.AimArcFor(pirate) * 180f / MathF.PI, 3);
        Assert.Equal(BroadsideVolley.PlayerAimArcDegrees, BroadsideVolley.AimArcFor(Gunner().ship) * 180f / MathF.PI, 3);
    }

    [Fact]
    public void APlayersBalls_FlyWhereTheyreLaid_AtFullSail_AndAPiratesCarryItsWay()
    {
        var (world, ship) = Gunner();
        var pirate = world.SpawnShip(new Vector2(32, 10), 0f, ShipStats.PirateSloop, null, new Ability?[] { new BroadsideVolley(), null, null, null });
        ship.Speed = ship.Stats.MaxSpeed;
        pirate.Speed = pirate.Stats.MaxSpeed;

        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + new Vector2(0, -20)); // empty water abeam to port
        Assert.All(world.Projectiles, p => Assert.Equal(0f, p.Velocity.X, 3));

        Assert.True(world.TryCastAbility(pirate, AbilitySlot.One, pirate.Position + new Vector2(0, -20)));
        world.Step(); // the pirate's lays a warning, then fires
        for (var i = 0; i < BroadsideVolley.PirateWindupTicks + 2; i++)
            world.Step();
        Assert.Contains(world.Projectiles, p => p.OwnerShipId == pirate.Id && p.Velocity.X > 1f);
    }

    [Fact]
    public void APlayersBroadside_IsLaidOnTheEnemyNearestTheCursor()
    {
        var (world, ship) = Gunner();
        var near = world.SpawnShip(ship.Position + ForeOfStarboardBeam(25f, 5f), 0f, ShipStats.PirateSloop);
        var far = world.SpawnShip(ship.Position + ForeOfStarboardBeam(-25f, 6f), 0f, ShipStats.PirateSloop);
        near.IsAnchored = far.IsAnchored = true;

        // Aimed at empty water abeam, but closer to the forward pirate: laid right on it.
        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + ForeOfStarboardBeam(10f, 5f));

        var expected = DegreesForeOfStarboardBeam(near.Position - ship.Position);
        Assert.All(world.Projectiles, p => Assert.Equal(expected, DegreesForeOfStarboardBeam(p.Velocity), 1));
        Assert.True(BroadsideVolley.FindMark(world, ship, BroadsideSide.Starboard, far.Position) is { Target: var t } && t == far);
    }

    [Fact]
    public void APlayersBroadside_LeadsASailingShip_AndHitsIt()
    {
        var (world, ship) = Gunner();
        var target = world.SpawnShip(ship.Position + new Vector2(-2f, 6f), 0f, ShipStats.PirateSloop); // abeam, sailing the same way
        target.Speed = target.Stats.MaxSpeed;
        target.Throttle = ShipMovement.ThrottleLevels;
        var health = target.Health;

        // The cursor sits on where the target is now; the guns go well ahead of it, to where it'll be.
        var bearing = DegreesForeOfStarboardBeam(target.Position - ship.Position);
        world.TryCastAbility(ship, AbilitySlot.One, target.Position);
        Assert.All(world.Projectiles, p => Assert.True(DegreesForeOfStarboardBeam(p.Velocity) > bearing + 10f,
            $"laid {DegreesForeOfStarboardBeam(p.Velocity)}, target bears {bearing}"));
        for (var i = 0; i < SimConstants.TickRate; i++)
            world.Step();

        Assert.True(target.Health < health);
    }

    [Fact]
    public void APlayersBroadside_NeverLocksOntoACrewmate_EvenWithFriendlyFire()
    {
        var (world, ship) = Gunner();
        world.FriendlyFire = true;
        world.SpawnShip(ship.Position + new Vector2(0f, 4f), 0f, ShipStats.Sloop, 2);

        Assert.Null(BroadsideVolley.FindMark(world, ship, BroadsideSide.Starboard, ship.Position + new Vector2(0f, 4f)));
    }

    [Fact]
    public void FindMark_SkipsEnemiesBeyondTheArcOrRange()
    {
        var (world, ship) = Gunner();
        world.SpawnShip(ship.Position + new Vector2(6f, 1f), 0f, ShipStats.PirateSloop).IsAnchored = true; // off the bow
        world.SpawnShip(ship.Position + new Vector2(0f, BroadsideVolley.Range + 3f), 0f, ShipStats.PirateSloop).IsAnchored = true;

        Assert.Null(BroadsideVolley.FindMark(world, ship, BroadsideSide.Starboard, ship.Position));
        Assert.Null(BroadsideVolley.FindMark(world, ship, BroadsideSide.Port, ship.Position));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ALaggingPlayersShot_StrikesShipsWhereTheyWereOnTheirScreen(bool sentWithViewTick)
    {
        var (world, ship) = Gunner();
        var target = world.SpawnShip(ship.Position + new Vector2(0, 3), 0f, ShipStats.PirateSloop);
        target.IsAnchored = true;
        for (var i = 0; i < 20; i++)
            world.Step();

        // The target has just got clear of the lane, but the player (seeing the world 14 ticks behind) fires at it.
        target.Position += new Vector2(30, 0);
        world.Step();
        var health = target.Health;
        world.Enqueue(new ShipGame.Shared.Commands.CastAbilityCommand(1, AbilitySlot.One, ship.Position + new Vector2(0, 3),
            sentWithViewTick ? world.Tick - 14 : null));
        for (var i = 0; i < 10; i++)
            world.Step();

        Assert.Equal(sentWithViewTick, target.Health < health);
    }
}
