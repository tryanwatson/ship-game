using System.Numerics;

namespace ShipGame.Shared.Simulation;

/// <summary>
/// Map bearings. North is "up" on screen, which is world -Y (the plane is Y-down, like the screen); east is
/// screen-right, world +X. So south-west, for example, is (-1, 1) normalized.
/// </summary>
public static class Compass
{
    private static readonly Vector2 North = new(0f, -1f);
    private static readonly Vector2 East = new(1f, 0f);

    public const float SouthWest = 225f;

    /// <summary>Unit world vector for a bearing in degrees clockwise from north.</summary>
    public static Vector2 Direction(float bearingDegrees)
    {
        var radians = bearingDegrees * MathF.PI / 180f;
        return North * MathF.Cos(radians) + East * MathF.Sin(radians);
    }

    /// <summary>Bearing in degrees clockwise from north, in [0, 360).</summary>
    public static float Bearing(Vector2 direction)
    {
        var degrees = MathF.Atan2(Vector2.Dot(direction, East), Vector2.Dot(direction, North)) * 180f / MathF.PI;
        return degrees < 0f ? degrees + 360f : degrees;
    }
}
