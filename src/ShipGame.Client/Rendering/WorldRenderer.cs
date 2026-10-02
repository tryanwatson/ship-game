using System;
using System.Linq;
using Microsoft.Xna.Framework;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>Draws the world with placeholder geometry: a water grid and flat hull polygons.</summary>
public sealed class WorldRenderer
{
    private const float MastHeight = 36f;
    private const float CannonballHeight = 10f;
    private const float HealthBarWidth = 44f;
    private const float HealthBarHeight = 5f;

    private static readonly Color Water = new(22, 64, 104);
    private static readonly Color OutOfBoundsWater = new(14, 40, 68);
    private static readonly Color GridLine = new Color(255, 255, 255) * 0.06f;
    private static readonly Color PlayerHull = new(176, 130, 82);
    private static readonly Color NpcHull = new(120, 120, 128);
    private static readonly Color TargetedHull = new(200, 70, 60);
    private static readonly Color HullOutline = new(30, 20, 12);
    private static readonly Color LaneReady = new Color(255, 230, 150) * 0.14f;
    private static readonly Color LaneCooling = new Color(255, 230, 150) * 0.04f;
    private static readonly Color Shallows = new(60, 120, 150);
    private static readonly Color Sand = new(214, 196, 140);
    private static readonly Color Grass = new(92, 140, 70);
    private static readonly Color Shoreline = new(120, 100, 60);
    private static readonly Color HutWallLit = new(170, 120, 70);
    private static readonly Color HutWallShade = new(120, 82, 48);
    private static readonly Color HutRoof = new(170, 60, 50);
    private static readonly Color HutRoofShade = new(125, 42, 36);
    private static readonly Color AggroRing = new Color(230, 80, 60) * 0.35f;
    private static readonly Color MoveMarker = new Color(120, 255, 140) * 0.8f;
    private static readonly Color Cannonball = new(20, 20, 24);
    private static readonly Color Shadow = new Color(0, 0, 0) * 0.3f;
    private static readonly Color AnchorRode = new(40, 45, 50);
    private static readonly Color AnchorRipple = new Color(220, 235, 245) * 0.6f;
    private static readonly Color AnchorMark = new(170, 220, 255);
    private static readonly Color HealthBack = new Color(0, 0, 0) * 0.6f;
    private static readonly Color HealthOwn = new(90, 200, 90);
    private static readonly Color HealthEnemy = new(210, 70, 60);

    private readonly PrimitiveBatch _batch;

    public WorldRenderer(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public void Draw(World world, float alpha, int localPlayerId, Matrix view)
    {
        _batch.Begin(view);

        DrawWater(world.WorldSize);
        foreach (var island in world.Islands)
            DrawIsland(island);

        // Guarding pirates show how close you can get before they come for you.
        foreach (var ship in world.Ships)
        {
            if (ship.Behavior is HunterBehavior { State: HunterState.Guarding })
                DrawGroundCircle(NVector2.Lerp(ship.PreviousPosition, ship.Position, alpha), HunterBehavior.AggroRange, AggroRing);
        }

        var localShip = world.GetPlayerShip(localPlayerId);
        if (localShip is not null)
            DrawLocalShipOverlays(localShip, alpha);
        _batch.Flush();

        var drawOrder = world.Ships
            .Select(ship => (ship, pos: NVector2.Lerp(ship.PreviousPosition, ship.Position, alpha)))
            .OrderBy(x => IsoProjection.Depth(x.pos));

        foreach (var (ship, pos) in drawOrder)
        {
            var heading = Angles.Lerp(ship.PreviousHeading, ship.Heading, alpha);
            var color = ship.OwnerPlayerId is not null ? PlayerHull
                : IsInFiringLane(localShip, ship) ? TargetedHull
                : NpcHull;

            if (ship.IsAnchored)
                DrawAnchorRode(ship, pos, heading);
            DrawShip(ship, pos, heading, color);
            _batch.Flush(); // Flush per ship so nearer hulls overlap farther ones.
        }

        // Cannonballs fly above the hulls, so they draw last.
        foreach (var projectile in world.Projectiles)
            DrawCannonball(NVector2.Lerp(projectile.PreviousPosition, projectile.Position, alpha));

        // Health bars float above everything, League-style.
        foreach (var ship in world.Ships)
            DrawHealthBar(ship, NVector2.Lerp(ship.PreviousPosition, ship.Position, alpha), ship == localShip);

        _batch.Flush();
    }

    private void DrawWater(NVector2 size)
    {
        var margin = new NVector2(World.OutOfBoundsMargin);
        FillWorldRect(-margin, size + margin, OutOfBoundsWater);
        FillWorldRect(NVector2.Zero, size, Water);

        for (var x = 0; x <= (int)size.X; x++)
            _batch.Line(IsoProjection.WorldToIso(new NVector2(x, 0)), IsoProjection.WorldToIso(new NVector2(x, size.Y)), GridLine);
        for (var y = 0; y <= (int)size.Y; y++)
            _batch.Line(IsoProjection.WorldToIso(new NVector2(0, y)), IsoProjection.WorldToIso(new NVector2(size.X, y)), GridLine);
    }

    /// <summary>
    /// Flat placeholder island: a pale shallows ring, a sand beach (the actual collision outline), and a grassy
    /// interior. Each layer is the outline scaled about the island's center, which stays convex.
    /// </summary>
    private void DrawIsland(Island island)
    {
        var outline = island.Outline;
        Span<Vector2> layer = stackalloc Vector2[outline.Length];

        ScaledOutline(island, outline, 1f + 1.2f / island.BoundingRadius, layer);
        _batch.FillConvex(layer, Shallows);

        ScaledOutline(island, outline, 1f, layer);
        _batch.FillConvex(layer, Sand);
        _batch.Outline(layer, Shoreline);

        ScaledOutline(island, outline, 0.72f, layer);
        _batch.FillConvex(layer, Grass);

        if (island.HasShipyard)
            DrawShipyardHut(island.Center);
    }

    /// <summary>A little isometric boathouse marking a shipyard: two lit/shaded walls and a pyramid roof.</summary>
    private void DrawShipyardHut(NVector2 center)
    {
        const float half = 1.2f;
        const float wallHeight = 16f;
        const float roofHeight = 14f;
        var top = IsoProjection.WorldToIso(center + new NVector2(-half, -half));
        var right = IsoProjection.WorldToIso(center + new NVector2(half, -half));
        var bottom = IsoProjection.WorldToIso(center + new NVector2(half, half));
        var left = IsoProjection.WorldToIso(center + new NVector2(-half, half));
        var up = new Vector2(0, -wallHeight);
        var apex = IsoProjection.WorldToIso(center) + up - new Vector2(0, roofHeight);

        Span<Vector2> face = stackalloc Vector2[4];
        face[0] = left; face[1] = bottom; face[2] = bottom + up; face[3] = left + up;
        _batch.FillConvex(face, HutWallLit);
        face[0] = bottom; face[1] = right; face[2] = right + up; face[3] = bottom + up;
        _batch.FillConvex(face, HutWallShade);

        Span<Vector2> roof = stackalloc Vector2[3];
        roof[0] = left + up; roof[1] = bottom + up; roof[2] = apex;
        _batch.FillConvex(roof, HutRoof);
        roof[0] = bottom + up; roof[1] = right + up; roof[2] = apex;
        _batch.FillConvex(roof, HutRoofShade);
        roof[0] = top + up; roof[1] = left + up; roof[2] = apex;
        _batch.FillConvex(roof, HutRoof);
    }

    private static void ScaledOutline(Island island, ReadOnlySpan<NVector2> outline, float scale, Span<Vector2> projected)
    {
        for (var i = 0; i < outline.Length; i++)
            projected[i] = IsoProjection.WorldToIso(island.Center + (outline[i] - island.Center) * scale);
    }

    /// <summary>Outline of a circle on the water (an ellipse on screen).</summary>
    private void DrawGroundCircle(NVector2 center, float radius, Color color)
    {
        Span<Vector2> points = stackalloc Vector2[64];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = MathF.Tau * i / points.Length;
            points[i] = IsoProjection.WorldToIso(center + new NVector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }
        _batch.Outline(points, color);
    }

    private void FillWorldRect(NVector2 min, NVector2 max, Color color)
    {
        Span<Vector2> corners = stackalloc Vector2[]
        {
            IsoProjection.WorldToIso(min),
            IsoProjection.WorldToIso(new NVector2(max.X, min.Y)),
            IsoProjection.WorldToIso(max),
            IsoProjection.WorldToIso(new NVector2(min.X, max.Y)),
        };
        _batch.FillConvex(corners, color);
    }

    private void DrawLocalShipOverlays(Ship ship, float alpha)
    {
        var pos = NVector2.Lerp(ship.PreviousPosition, ship.Position, alpha);
        var heading = Angles.Lerp(ship.PreviousHeading, ship.Heading, alpha);

        foreach (var ability in ship.Abilities)
        {
            if (ability?.Definition is BroadsideVolley volley)
                DrawFiringLane(ship, volley, pos, heading, ability.IsReady ? LaneReady : LaneCooling);
        }

        if (ship.MoveTarget is { } target)
        {
            var t = IsoProjection.WorldToIso(target);
            _batch.Line(IsoProjection.WorldToIso(pos), t, MoveMarker * 0.4f);
            _batch.Line(t + new Vector2(-8, -4), t + new Vector2(8, 4), MoveMarker);
            _batch.Line(t + new Vector2(-8, 4), t + new Vector2(8, -4), MoveMarker);
        }
    }

    private void DrawFiringLane(Ship ship, BroadsideVolley volley, NVector2 pos, float heading, Color color)
    {
        var forward = new NVector2(MathF.Cos(heading), MathF.Sin(heading));
        var right = new NVector2(-forward.Y, forward.X);
        var outward = volley.Side == BroadsideSide.Starboard ? right : -right;

        var halfSpan = BroadsideVolley.HalfSpan(ship) + Projectile.Radius;
        var near = ship.Stats.Beam / 2f;
        var far = near + BroadsideVolley.RangeFor(ship);

        Span<Vector2> lane = stackalloc Vector2[]
        {
            IsoProjection.WorldToIso(pos + forward * halfSpan + outward * near),
            IsoProjection.WorldToIso(pos + forward * halfSpan + outward * far),
            IsoProjection.WorldToIso(pos - forward * halfSpan + outward * far),
            IsoProjection.WorldToIso(pos - forward * halfSpan + outward * near),
        };
        _batch.FillConvex(lane, color);
    }

    private void DrawShip(Ship ship, NVector2 pos, float heading, Color color)
    {
        // The same outline the simulation hits against, placed at the interpolated pose and projected.
        Span<NVector2> outline = stackalloc NVector2[HullShape.PointCount];
        HullShape.GetWorldOutline(pos, heading, ship.Stats, outline);
        Span<Vector2> hull = stackalloc Vector2[HullShape.PointCount];
        for (var i = 0; i < hull.Length; i++)
            hull[i] = IsoProjection.WorldToIso(outline[i]);

        _batch.FillConvex(hull, color);
        _batch.Outline(hull, HullOutline);

        var mastBase = IsoProjection.WorldToIso(pos);
        _batch.Line(mastBase, mastBase - new Vector2(0, MastHeight), HullOutline);
    }

    private void DrawCannonball(NVector2 pos)
    {
        var ground = IsoProjection.WorldToIso(pos);
        FillOctagon(ground, 4f, 2f, Shadow);
        FillOctagon(ground - new Vector2(0, CannonballHeight), 3f, 3f, Cannonball);
    }

    private void DrawHealthBar(Ship ship, NVector2 pos, bool isLocal)
    {
        var anchor = IsoProjection.WorldToIso(pos) - new Vector2(HealthBarWidth / 2f, MastHeight + 12f);
        var fraction = Math.Clamp(ship.Health / ship.Stats.MaxHealth, 0f, 1f);

        FillRect(anchor, new Vector2(HealthBarWidth, HealthBarHeight), HealthBack);
        FillRect(anchor + Vector2.One, new Vector2((HealthBarWidth - 2f) * fraction, HealthBarHeight - 2f),
            isLocal ? HealthOwn : HealthEnemy);

        if (ship.IsAnchored)
            DrawAnchorMark(anchor + new Vector2(-9f, HealthBarHeight / 2f));
    }

    /// <summary>The anchor line: from the bow down to a ripple a little ahead, where the anchor bit.</summary>
    private void DrawAnchorRode(Ship ship, NVector2 pos, float heading)
    {
        var forward = new NVector2(MathF.Cos(heading), MathF.Sin(heading));
        var bow = pos + forward * (ship.Stats.Length / 2f);
        var anchorPoint = bow + forward * 1.1f;
        _batch.Line(IsoProjection.WorldToIso(bow), IsoProjection.WorldToIso(anchorPoint), AnchorRode);

        Span<Vector2> ripple = stackalloc Vector2[12];
        for (var i = 0; i < ripple.Length; i++)
        {
            var angle = MathF.Tau * i / ripple.Length;
            ripple[i] = IsoProjection.WorldToIso(anchorPoint + new NVector2(MathF.Cos(angle), MathF.Sin(angle)) * 0.3f);
        }
        _batch.Outline(ripple, AnchorRipple);
    }

    /// <summary>A small anchor icon (shank, stock, arms) centered on <paramref name="center"/>, beside the health bar.</summary>
    private void DrawAnchorMark(Vector2 center)
    {
        _batch.Line(center + new Vector2(0, -5), center + new Vector2(0, 5), AnchorMark);
        _batch.Line(center + new Vector2(-3, -3), center + new Vector2(3, -3), AnchorMark);
        _batch.Line(center + new Vector2(-5, 2), center + new Vector2(0, 5), AnchorMark);
        _batch.Line(center + new Vector2(0, 5), center + new Vector2(5, 2), AnchorMark);
    }

    private void FillRect(Vector2 topLeft, Vector2 size, Color color)
    {
        Span<Vector2> rect = stackalloc Vector2[]
        {
            topLeft,
            topLeft + new Vector2(size.X, 0),
            topLeft + size,
            topLeft + new Vector2(0, size.Y),
        };
        _batch.FillConvex(rect, color);
    }

    private void FillOctagon(Vector2 center, float radiusX, float radiusY, Color color)
    {
        Span<Vector2> points = stackalloc Vector2[8];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = MathF.Tau * i / points.Length;
            points[i] = center + new Vector2(MathF.Cos(angle) * radiusX, MathF.Sin(angle) * radiusY);
        }
        _batch.FillConvex(points, color);
    }

    private static bool IsInFiringLane(Ship? attacker, Ship target) =>
        attacker is not null
        && attacker.Abilities.Any(a => a?.Definition is BroadsideVolley volley
                                       && volley.Covers(attacker, target.Position, target.Stats.Radius));
}
