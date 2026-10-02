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
    public static bool SegmentHits(Ship ship, Vector2 from, Vector2 to, float radius)
    {
        // Cheap reject: the path can't reach anything within the hull's bounding circle.
        var reach = ship.Stats.Length / 2f + radius;
        if (DistanceToSegment(ship.Position, from, to) > reach)
            return false;

        Span<Vector2> hull = stackalloc Vector2[PointCount];
        GetWorldOutline(ship.Position, ship.Heading, ship.Stats, hull);

        if (Contains(hull, from) || Contains(hull, to))
            return true;

        for (var i = 0; i < PointCount; i++)
        {
            if (SegmentDistance(from, to, hull[i], hull[(i + 1) % PointCount]) <= radius)
                return true;
        }
        return false;
    }

    private static bool Contains(ReadOnlySpan<Vector2> convex, Vector2 point)
    {
        var sign = 0;
        for (var i = 0; i < convex.Length; i++)
        {
            var a = convex[i];
            var b = convex[(i + 1) % convex.Length];
            var cross = Cross(b - a, point - a);
            var s = cross > 0f ? 1 : cross < 0f ? -1 : 0;
            if (s == 0)
                continue;
            if (sign == 0)
                sign = s;
            else if (s != sign)
                return false;
        }
        return true;
    }

    private static float SegmentDistance(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        if (SegmentsIntersect(p1, p2, q1, q2))
            return 0f;
        return MathF.Min(
            MathF.Min(DistanceToSegment(p1, q1, q2), DistanceToSegment(p2, q1, q2)),
            MathF.Min(DistanceToSegment(q1, p1, p2), DistanceToSegment(q2, p1, p2)));
    }

    private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        var d1 = Cross(q2 - q1, p1 - q1);
        var d2 = Cross(q2 - q1, p2 - q1);
        var d3 = Cross(p2 - p1, q1 - p1);
        var d4 = Cross(p2 - p1, q2 - p1);
        return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        var t = lengthSquared < 1e-12f ? 0f : Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, a + ab * t);
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
