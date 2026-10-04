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
    [InlineData(50f, 15f)]   // well forward: held at the edge of the window
    [InlineData(-80f, -15f)]
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
        Assert.False(BroadsideVolley.Covers(ship, BroadsideSide.Starboard, ship.Position + ForeOfStarboardBeam(40f, reach), 0.2f));
        Assert.False(BroadsideVolley.Covers(ship, BroadsideSide.Starboard, ship.Position + ForeOfStarboardBeam(0f, reach + 2f), 0.2f));
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
