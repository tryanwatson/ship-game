using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

public class HullHitTests
{
    // Sloop: length 2.4 (bow tip at +1.2), beam 0.9 (sides at +/-0.45 amidships), collision circle radius 1.
    private static Ship ShipAt(float heading = 0f) =>
        new(1, null, ShipStats.Sloop) { Position = new Vector2(30, 30), Heading = heading };

    /// <summary>A ball crossing the ship's centerline at <paramref name="along"/> (+ toward the bow), side to side.</summary>
    private static bool CrossingShotHits(Ship ship, float along, float stepLength = 0.47f)
    {
        var forward = ship.Forward;
        var side = new Vector2(-forward.Y, forward.X);
        var crossing = ship.Position + forward * along;

        // Walk the ball across in tick-sized steps, as the simulation does.
        for (var s = -3f; s < 3f; s += stepLength)
        {
            if (HullShape.SegmentHits(ship, crossing + side * s, crossing + side * (s + stepLength), Projectile.DefaultRadius))
                return true;
        }
        return false;
    }

    [Theory]
    [InlineData(1.1f)]   // through the nose: outside the old 1-tile circle, the reported bug
    [InlineData(-1.15f)] // through the stern, also outside the circle
    [InlineData(0f)]     // amidships
    public void ShotsThroughTheHull_Hit(float along)
    {
        Assert.True(CrossingShotHits(ShipAt(), along));
        Assert.True(CrossingShotHits(ShipAt(heading: 2.2f), along)); // any heading
    }

    [Theory]
    [InlineData(1.5f)]  // clear ahead of the bow
    [InlineData(-1.5f)] // clear astern
    public void ShotsPastTheEnds_Miss(float along)
    {
        Assert.False(CrossingShotHits(ShipAt(), along));
    }

    [Fact]
    public void ShotsSkimmingPastTheSide_Miss()
    {
        // Running fore-and-aft 0.8 off the centerline: inside the old circle, clear of the 0.9-wide hull.
        var ship = ShipAt();
        Assert.False(HullShape.SegmentHits(ship, ship.Position + new Vector2(-3f, 0.8f), ship.Position + new Vector2(3f, 0.8f), Projectile.DefaultRadius));
    }

    [Fact]
    public void FastShot_CannotTunnelThroughTheBow()
    {
        // One long step that starts in front of the narrow bow tip and ends beyond it.
        var ship = ShipAt();
        Assert.True(HullShape.SegmentHits(ship, ship.Position + new Vector2(1.1f, -2f), ship.Position + new Vector2(1.1f, 2f), Projectile.DefaultRadius));
    }

    [Fact]
    public void Volley_ThroughTheNose_DealsDamage()
    {
        // End to end: a target lying bow-on across the lane, nose just inside the outermost cannon's line.
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        var shooter = world.SpawnShip(new Vector2(30, 30), 0f, ShipStats.Sloop, 1, ShipGame.Shared.Abilities.Loadouts.FullArsenal);
        // Outermost cannon fires along x = 30 + HalfSpan (0.72). Target points at it from ahead, bow tip at x ~ 30.8.
        var target = world.SpawnShip(new Vector2(32f, 34f), MathF.PI, ShipStats.Sloop);

        world.TryCastAbility(shooter, ShipGame.Shared.Abilities.AbilitySlot.One, shooter.Position + new Vector2(0, 5));
        for (var t = 0; t < SimConstants.TickRate; t++)
            world.Step();

        Assert.True(target.Health < target.Stats.MaxHealth);
    }
}
