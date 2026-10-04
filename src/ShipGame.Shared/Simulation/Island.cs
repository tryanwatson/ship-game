using System.Numerics;

namespace ShipGame.Shared.Simulation;

/// <summary>A landmass: a convex outline in world space that ships can't sail through and shots can't pass.</summary>
public sealed class Island
{
    private readonly Vector2[] _outline;

    /// <summary>Gold for plundering an island unless its layout says otherwise.</summary>
    public const int DefaultPlunderGold = 10;

    public Island(int id, IReadOnlyList<Vector2> outline, int plunderGold = DefaultPlunderGold, bool hasShipyard = false, string? name = null,
        int level = 1)
    {
        Name = name ?? $"ISLE {id}";
        Level = level;
        PlunderGold = plunderGold;
        HasShipyard = hasShipyard;
        if (outline.Count < 3)
            throw new ArgumentException("An island needs at least three points.", nameof(outline));

        Id = id;
        _outline = outline.ToArray();
        if (!Geometry.IsConvex(_outline))
            throw new ArgumentException("Island outlines must be convex.", nameof(outline));

        Center = Geometry.Centroid(_outline);
        Area = Geometry.Area(_outline);
        BoundingRadius = _outline.Max(p => Vector2.Distance(p, Center));
    }

    public int Id { get; }

    /// <summary>What the charts call it (upper case, for the pixel font): how trade contracts name their destinations.</summary>
    public string Name { get; }

    /// <summary>Gold a player earns for plundering this island.</summary>
    public int PlunderGold { get; }

    /// <summary>The level of the waters it lies in (see <c>Archipelago.Seas</c>): richer plunder, and better shipyard stock.</summary>
    public int Level { get; }

    /// <summary>Players anchored here can spend gold on upgrades; plundering it is opt-in.</summary>
    public bool HasShipyard { get; }

    public ReadOnlySpan<Vector2> Outline => _outline;

    public Vector2 Center { get; }

    /// <summary>Surface area in square tiles.</summary>
    public float Area { get; }

    /// <summary>Distance from <see cref="Center"/> to the farthest point of the outline, for cheap rejection.</summary>
    public float BoundingRadius { get; }

    /// <summary>Distance from a point to the shore; 0 on land.</summary>
    public float DistanceTo(Vector2 point) => Geometry.DistanceToConvex(_outline, point);

    /// <summary>
    /// An island built from a hand-drawn outline: rotated, and scaled so its area is exactly
    /// <paramref name="area"/> square tiles, then centered on <paramref name="center"/>.
    /// </summary>
    public static Island FromTemplate(int id, IReadOnlyList<Vector2> template, Vector2 center, float area, float rotation, bool hasShipyard = false,
        string? name = null, int plunderGold = DefaultPlunderGold, int level = 1)
    {
        var templateCenter = Geometry.Centroid(template.ToArray());
        var scale = MathF.Sqrt(area / Geometry.Area(template.ToArray()));
        var cos = MathF.Cos(rotation);
        var sin = MathF.Sin(rotation);
        var outline = template.Select(p =>
        {
            var local = (p - templateCenter) * scale;
            return center + new Vector2(local.X * cos - local.Y * sin, local.X * sin + local.Y * cos);
        }).ToList();
        return new Island(id, outline, plunderGold, hasShipyard, name, level);
    }
}
