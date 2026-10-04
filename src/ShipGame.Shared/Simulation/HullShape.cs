using System.Numerics;

namespace ShipGame.Shared.Simulation;

/// <summary>
/// The hull outline: a convex pentagon with a pointed bow, sized by <see cref="ShipStats.Length"/> and
/// <see cref="ShipStats.Beam"/>. The one definition used both for hit detection and for drawing, so what you
/// see is what you can hit.
/// </summary>
public static class HullShape
{
    public const int PointCount = 5;

    /// <summary>Outline in ship-local space (+X is the bow), in winding order.</summary>
    public static void GetLocalOutline(ShipStats stats, Span<Vector2> outline)
    {
        var length = stats.Length;
        var beam = stats.Beam;
        outline[0] = new Vector2(length * 0.5f, 0f);              // bow
        outline[1] = new Vector2(length * 0.15f, beam * 0.5f);    // starboard shoulder
        outline[2] = new Vector2(-length * 0.5f, beam * 0.4f);    // starboard quarter
        outline[3] = new Vector2(-length * 0.5f, -beam * 0.4f);   // port quarter
        outline[4] = new Vector2(length * 0.15f, -beam * 0.5f);   // port shoulder
    }

    /// <summary>Outline placed at <paramref name="position"/> facing <paramref name="heading"/>.</summary>
    public static void GetWorldOutline(Vector2 position, float heading, ShipStats stats, Span<Vector2> outline)
    {
        GetLocalOutline(stats, outline);
        var cos = MathF.Cos(heading);
        var sin = MathF.Sin(heading);
        for (var i = 0; i < PointCount; i++)
        {
            var p = outline[i];
            outline[i] = position + new Vector2(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos);
        }
    }

    /// <summary>
    /// Whether a ball of <paramref name="radius"/> travelling from <paramref name="from"/> to
    /// <paramref name="to"/> this tick touches the hull. Testing the whole path rather than the end point
    /// stops fast shots skipping through the narrow bow between ticks.
    /// </summary>
    public static bool SegmentHits(Ship ship, Vector2 from, Vector2 to, float radius) =>
        SegmentHits(ship, ship.Position, ship.Heading, from, to, radius);

    /// <summary>As <see cref="SegmentHits(Ship, Vector2, Vector2, float)"/>, with the hull at another position and heading.</summary>
    public static bool SegmentHits(Ship ship, Vector2 position, float heading, Vector2 from, Vector2 to, float radius)
    {
        // Cheap reject: the path can't reach anything within the hull's bounding circle.
        var reach = ship.Stats.Length / 2f + radius;
        if (Geometry.DistanceToSegment(position, from, to) > reach)
            return false;

        Span<Vector2> hull = stackalloc Vector2[PointCount];
        GetWorldOutline(position, heading, ship.Stats, hull);
        return Geometry.SegmentTouchesConvex(hull, from, to, radius);
    }
}
