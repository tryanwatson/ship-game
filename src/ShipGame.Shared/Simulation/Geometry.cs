using System.Numerics;

namespace ShipGame.Shared.Simulation;

/// <summary>2D convex-polygon helpers shared by hulls and islands.</summary>
public static class Geometry
{
    public static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>True if <paramref name="point"/> is inside (or on) a convex polygon of either winding.</summary>
    public static bool ConvexContains(ReadOnlySpan<Vector2> convex, Vector2 point)
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

    /// <summary>Distance from a point to a convex polygon; 0 inside it.</summary>
    public static float DistanceToConvex(ReadOnlySpan<Vector2> convex, Vector2 point)
    {
        if (ConvexContains(convex, point))
            return 0f;
        var nearest = float.MaxValue;
        for (var i = 0; i < convex.Length; i++)
            nearest = MathF.Min(nearest, DistanceToSegment(point, convex[i], convex[(i + 1) % convex.Length]));
        return nearest;
    }

    /// <summary>Whether a ball of <paramref name="radius"/> moving from <paramref name="from"/> to <paramref name="to"/> touches a convex polygon.</summary>
    public static bool SegmentTouchesConvex(ReadOnlySpan<Vector2> convex, Vector2 from, Vector2 to, float radius)
    {
        if (ConvexContains(convex, from) || ConvexContains(convex, to))
            return true;
        for (var i = 0; i < convex.Length; i++)
        {
            if (SegmentDistance(from, to, convex[i], convex[(i + 1) % convex.Length]) <= radius)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Separating-axis test between two convex polygons. On overlap, returns the unit normal and depth that
    /// pushes <paramref name="a"/> out of <paramref name="b"/> by the shortest route (the normal points from b toward a).
    /// </summary>
    public static bool TryGetPenetration(ReadOnlySpan<Vector2> a, ReadOnlySpan<Vector2> b, out Vector2 normal, out float depth)
    {
        normal = Vector2.Zero;
        depth = float.MaxValue;

        for (var pass = 0; pass < 2; pass++)
        {
            var edges = pass == 0 ? a : b;
            for (var i = 0; i < edges.Length; i++)
            {
                var edge = edges[(i + 1) % edges.Length] - edges[i];
                if (edge.LengthSquared() < 1e-12f)
                    continue;
                var axis = Vector2.Normalize(new Vector2(-edge.Y, edge.X));

                Project(a, axis, out var minA, out var maxA);
                Project(b, axis, out var minB, out var maxB);
                var overlap = MathF.Min(maxA, maxB) - MathF.Max(minA, minB);
                if (overlap <= 0f)
                    return false; // separating axis found

                if (overlap < depth)
                {
                    depth = overlap;
                    normal = axis;
                }
            }
        }

        // Orient the normal from b toward a.
        if (Vector2.Dot(Centroid(a) - Centroid(b), normal) < 0f)
            normal = -normal;
        return true;
    }

    public static Vector2 Centroid(ReadOnlySpan<Vector2> polygon)
    {
        var sum = Vector2.Zero;
        foreach (var p in polygon)
            sum += p;
        return sum / polygon.Length;
    }

    /// <summary>Area of a simple polygon (either winding).</summary>
    public static float Area(ReadOnlySpan<Vector2> polygon)
    {
        var twice = 0f;
        for (var i = 0; i < polygon.Length; i++)
            twice += Cross(polygon[i], polygon[(i + 1) % polygon.Length]);
        return MathF.Abs(twice) / 2f;
    }

    public static bool IsConvex(ReadOnlySpan<Vector2> polygon)
    {
        var sign = 0;
        for (var i = 0; i < polygon.Length; i++)
        {
            var cross = Cross(polygon[(i + 1) % polygon.Length] - polygon[i], polygon[(i + 2) % polygon.Length] - polygon[(i + 1) % polygon.Length]);
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

    public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        var t = lengthSquared < 1e-12f ? 0f : Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, a + ab * t);
    }

    public static float SegmentDistance(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
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

    private static void Project(ReadOnlySpan<Vector2> polygon, Vector2 axis, out float min, out float max)
    {
        min = float.MaxValue;
        max = float.MinValue;
        foreach (var p in polygon)
        {
            var d = Vector2.Dot(p, axis);
            min = MathF.Min(min, d);
            max = MathF.Max(max, d);
        }
    }
}
