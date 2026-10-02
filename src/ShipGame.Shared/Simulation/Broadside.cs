using System.Numerics;

namespace ShipGame.Shared.Simulation;

public enum BroadsideSide
{
    None,
    Port,
    Starboard,
}

public static class Broadside
{
    public const float DefaultHalfArc = MathF.PI / 6f; // 30 degrees either side of abeam

    /// <summary>
    /// Which side of the ship, if any, <paramref name="target"/> lies abeam of.
    /// Range is not checked here; abilities apply their own.
    /// </summary>
    public static BroadsideSide GetSide(Ship ship, Vector2 target, float halfArc = DefaultHalfArc)
    {
        var toTarget = target - ship.Position;
        if (toTarget.LengthSquared() < 1e-8f)
            return BroadsideSide.None;

        var relative = Angles.Delta(ship.Heading, MathF.Atan2(toTarget.Y, toTarget.X));

        // Y-down world: +90 degrees from the bow is starboard (right), -90 is port (left).
        if (MathF.Abs(Angles.Delta(relative, MathF.PI / 2f)) <= halfArc)
            return BroadsideSide.Starboard;
        if (MathF.Abs(Angles.Delta(relative, -MathF.PI / 2f)) <= halfArc)
            return BroadsideSide.Port;
        return BroadsideSide.None;
    }
}
