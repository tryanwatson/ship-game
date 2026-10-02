using System.Numerics;

namespace ShipGame.Shared.Simulation;

/// <summary>
/// Map bearings. North is "up" on screen, which in the Y-down world plane under the isometric projection is
/// (-1, -1); east is screen-right, (1, -1). So south-west, for example, is world +Y.
/// </summary>
public static class Compass
{
    private static readonly Vector2 North = Vector2.Normalize(new Vector2(-1f, -1f));
    private static readonly Vector2 East = Vector2.Normalize(new Vector2(1f, -1f));

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
