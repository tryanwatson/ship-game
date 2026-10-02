using System.Numerics;
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
}
