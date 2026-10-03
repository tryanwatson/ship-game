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
    // Broadside readiness ring (cf. Urgot's legs): a glowing arc per loaded deck, dark and refilling while reloading.
    private static readonly Color DeckLoaded = new Color(255, 150, 60) * 0.75f;
    private static readonly Color DeckLoadedEdge = new(255, 205, 120);
    private static readonly Color DeckEmpty = new Color(0, 0, 0) * 0.45f;
    private static readonly Color DeckRefill = new Color(255, 150, 60) * 0.3f;
    // Hugs the hull: the beam is 0.45 tiles out from the centerline, so this sits just outside the sides.
    private const float DeckRingInner = 0.75f;
    private const float DeckRingOuter = 0.95f;
    private const float DeckArcHalfDegrees = 20f;

    private static readonly Color AimFill = new Color(170, 220, 255) * 0.18f;
    private static readonly Color AimEdge = new Color(170, 220, 255) * 0.6f;
    private static readonly Color AimCooling = new Color(150, 150, 160) * 0.35f;
    private static readonly Color StrikeWarning = new Color(235, 80, 60) * 0.16f;
    private static readonly Color StrikeFill = new Color(235, 80, 60) * 0.3f;
    private static readonly Color StrikeEdge = new Color(240, 110, 80) * 0.8f;
    private static readonly Color Shell = new(25, 25, 30);
    private static readonly Color LongGunShot = new(60, 50, 40);
    private static readonly Color ShotTrail = new Color(230, 230, 235) * 0.35f;
    private static readonly Color Blast = new(255, 190, 90);
    private const float ShellArcHeight = 60f;
    private const float BlastSeconds = 0.45f;

    private readonly System.Collections.Generic.List<(NVector2 Center, float Radius, float Age)> _blasts = new();

    /// <summary>A shell burst at <paramref name="center"/>: drawn as a ring expanding to the blast radius.</summary>
    public void AddBlast(NVector2 center, float radius) => _blasts.Add((center, radius, 0f));

    public void UpdateEffects(float elapsedSeconds)
    {
        for (var i = _blasts.Count - 1; i >= 0; i--)
        {
            var blast = _blasts[i];
            blast.Age += elapsedSeconds;
            if (blast.Age >= BlastSeconds)
                _blasts.RemoveAt(i);
            else
                _blasts[i] = blast;
        }
    }

    private static readonly Color HealthBack = new Color(0, 0, 0) * 0.6f;
    private static readonly Color HealthOwn = new(90, 200, 90);
    private static readonly Color HealthEnemy = new(210, 70, 60);

    private readonly PrimitiveBatch _batch;

    public WorldRenderer(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public void Draw(World world, float alpha, int localPlayerId, Matrix view, AimPreview? aim = null)
    {
        // Shells and warnings run on ticks; alpha is how far we are into the latest one.
        var renderTick = world.Tick - 1 + alpha;
        _batch.Begin(view);

        DrawWater(world.WorldSize);
        foreach (var island in world.Islands)
            DrawIsland(island);

        // Guarding pirates show how close you can get before they come for you.
        foreach (var ship in world.Ships)
        {
            if (ship.Stance == NpcStance.Guarding)
                DrawGroundCircle(NVector2.Lerp(ship.PreviousPosition, ship.Position, alpha), HunterBehavior.AggroRange, AggroRing);
        }

        // Where shells will land: public, so anyone can get out of the way.
        foreach (var strike in world.Strikes)
            DrawStrikeWarning(strike, strike.Progress(renderTick));

        var localShip = world.GetPlayerShip(localPlayerId);
        if (localShip is not null)
        {
            DrawLocalShipOverlays(localShip, alpha);
            if (aim is { } preview)
                DrawAimPreview(localShip, NVector2.Lerp(localShip.PreviousPosition, localShip.Position, alpha), preview);
        }
        _batch.Flush();

        var drawOrder = world.Ships
            .Select(ship => (ship, pos: NVector2.Lerp(ship.PreviousPosition, ship.Position, alpha)))
            .OrderBy(x => IsoProjection.Depth(x.pos));

        foreach (var (ship, pos) in drawOrder)
        {
            var heading = Angles.Lerp(ship.PreviousHeading, ship.Heading, alpha);
            // Anything our shots can hurt (pirates, and other players with friendly fire on) lights up in our lanes.
            var targetable = localShip is not null && world.CanDamage(localShip.Id, localShip.Team, ship);
            var color = targetable && IsInFiringLane(localShip, ship) ? TargetedHull
                : ship.OwnerPlayerId is not null ? PlayerHull
                : NpcHull;

            if (ship.IsAnchored)
                DrawAnchorRode(ship, pos, heading);
            if (ship == localShip)
                DrawBroadsideRing(ship, pos, heading);
            DrawShip(ship, pos, heading, color);
            _batch.Flush(); // Flush per ship so nearer hulls overlap farther ones.
        }

        // Cannonballs and shells fly above the hulls, so they draw last.
        foreach (var projectile in world.Projectiles)
            DrawCannonball(projectile, NVector2.Lerp(projectile.PreviousPosition, projectile.Position, alpha));
        foreach (var strike in world.Strikes)
            DrawShell(strike, strike.Progress(renderTick));
        foreach (var blast in _blasts)
            DrawBlast(blast.Center, blast.Radius, blast.Age / BlastSeconds);

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
    /// The broadside readiness ring on the water around a ship: an arc on each beam, centered where that deck fires.
    /// Loaded: glowing orange. Reloading: dark, refilling from the stern end toward the bow as the guns come ready.
    /// Only drawn for the local player's ship.
    /// </summary>
    private void DrawBroadsideRing(Ship ship, NVector2 pos, float heading)
    {
        if (ship.Abilities.FirstOrDefault(a => a?.Definition is BroadsideVolley) is not { } broadside)
            return;

        var halfArc = DeckArcHalfDegrees * MathF.PI / 180f;
        foreach (var side in new[] { BroadsideSide.Port, BroadsideSide.Starboard })
        {
            var channel = BroadsideVolley.ChannelOf(side);
            var beam = heading + (side == BroadsideSide.Starboard ? MathF.PI / 2f : -MathF.PI / 2f);

            // Arc runs from the stern end to the bow end of this beam, whichever way round that is.
            var sternEnd = side == BroadsideSide.Starboard ? beam + halfArc : beam - halfArc;
            var bowEnd = side == BroadsideSide.Starboard ? beam - halfArc : beam + halfArc;

            if (broadside.IsChannelReady(channel))
            {
                DrawRingArc(pos, sternEnd, bowEnd, DeckRingInner, DeckRingOuter, DeckLoaded);
                DrawRingArcEdge(pos, sternEnd, bowEnd, DeckRingOuter, DeckLoadedEdge);
            }
            else
            {
                var loaded = 1f - broadside.CooldownFraction(channel);
                DrawRingArc(pos, sternEnd, bowEnd, DeckRingInner, DeckRingOuter, DeckEmpty);
                DrawRingArc(pos, sternEnd, sternEnd + (bowEnd - sternEnd) * loaded, DeckRingInner, DeckRingOuter, DeckRefill);
            }
        }
    }

    /// <summary>A band of a ring on the water between two angles, built from small convex quads.</summary>
    private void DrawRingArc(NVector2 center, float from, float to, float inner, float outer, Color color)
    {
        const int maxSegments = 14;
        var span = MathF.Abs(to - from);
        if (span < 1e-4f)
            return;
        var segments = Math.Max(1, (int)MathF.Ceiling(maxSegments * span / (2f * DeckArcHalfDegrees * MathF.PI / 180f)));
        Span<Vector2> quad = stackalloc Vector2[4];
        for (var i = 0; i < segments; i++)
        {
            var a0 = from + (to - from) * i / segments;
            var a1 = from + (to - from) * (i + 1) / segments;
            var d0 = new NVector2(MathF.Cos(a0), MathF.Sin(a0));
            var d1 = new NVector2(MathF.Cos(a1), MathF.Sin(a1));
            quad[0] = IsoProjection.WorldToIso(center + d0 * inner);
            quad[1] = IsoProjection.WorldToIso(center + d0 * outer);
            quad[2] = IsoProjection.WorldToIso(center + d1 * outer);
            quad[3] = IsoProjection.WorldToIso(center + d1 * inner);
            _batch.FillConvex(quad, color);
        }
    }

    private void DrawRingArcEdge(NVector2 center, float from, float to, float radius, Color color)
    {
        const int segments = 14;
        for (var i = 0; i < segments; i++)
        {
            var a0 = from + (to - from) * i / segments;
            var a1 = from + (to - from) * (i + 1) / segments;
            _batch.Line(
                IsoProjection.WorldToIso(center + new NVector2(MathF.Cos(a0), MathF.Sin(a0)) * radius),
                IsoProjection.WorldToIso(center + new NVector2(MathF.Cos(a1), MathF.Sin(a1)) * radius),
                color);
        }
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

        if (ship.MoveTarget is { } target)
        {
            var t = IsoProjection.WorldToIso(target);
            _batch.Line(IsoProjection.WorldToIso(pos), t, MoveMarker * 0.4f);
            _batch.Line(t + new Vector2(-8, -4), t + new Vector2(8, 4), MoveMarker);
            _batch.Line(t + new Vector2(-8, 4), t + new Vector2(8, -4), MoveMarker);
        }
    }

    private void DrawFiringLane(Ship ship, BroadsideSide side, NVector2 pos, float heading, Color color)
    {
        var forward = new NVector2(MathF.Cos(heading), MathF.Sin(heading));
        var right = new NVector2(-forward.Y, forward.X);
        var outward = side == BroadsideSide.Starboard ? right : -right;

        var halfSpan = BroadsideVolley.HalfSpan(ship) + Projectile.DefaultRadius;
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

    private void DrawCannonball(Projectile projectile, NVector2 pos)
    {
        // Size the ball (and its shadow) by its hit radius: long gun shots are visibly heavier.
        var scale = projectile.Radius / Projectile.DefaultRadius;
        var ground = IsoProjection.WorldToIso(pos);
        var ball = ground - new Vector2(0, CannonballHeight);
        if (scale > 1.2f)
        {
            var tail = IsoProjection.WorldToIso(pos - projectile.Velocity * 0.06f) - new Vector2(0, CannonballHeight);
            _batch.Line(tail, ball, ShotTrail);
        }
        FillOctagon(ground, 4f * scale, 2f * scale, Shadow);
        FillOctagon(ball, 3f * scale, 3f * scale, scale > 1.2f ? LongGunShot : Cannonball);
    }

    /// <summary>The landing zone of a shell in the air: a red circle that fills in as impact nears.</summary>
    private void DrawStrikeWarning(AreaStrike strike, float progress)
    {
        FillGroundCircle(strike.Target, strike.Radius, StrikeWarning);
        FillGroundCircle(strike.Target, strike.Radius * progress, StrikeFill);
        DrawGroundCircle(strike.Target, strike.Radius, StrikeEdge);
    }

    /// <summary>The shell itself, arcing from where it was fired to where it lands, over its shadow on the water.</summary>
    private void DrawShell(AreaStrike strike, float progress)
    {
        var ground = NVector2.Lerp(strike.Origin, strike.Target, progress);
        var groundIso = IsoProjection.WorldToIso(ground);
        var height = 4f * ShellArcHeight * progress * (1f - progress);
        FillOctagon(groundIso, 4f, 2f, Shadow);
        FillOctagon(groundIso - new Vector2(0, height + CannonballHeight), 4f, 4f, Shell);
    }

    private void DrawBlast(NVector2 center, float radius, float t)
    {
        var fade = 1f - t;
        FillGroundCircle(center, radius * (0.3f + 0.7f * t), Blast * (0.35f * fade));
        DrawGroundCircle(center, radius * (0.5f + 0.6f * t), Blast * fade);
    }

    /// <summary>Targeting indicator for a held aimed ability: the long gun's path, or the mortar's reach and blast.</summary>
    private void DrawAimPreview(Ship ship, NVector2 pos, AimPreview aim)
    {
        var ability = ship.GetAbility(aim.Slot);
        var ready = ability is not null && ability.IsChannelReady(ability.Definition.ChannelFor(ship, aim.Cursor));
        switch (ability?.Definition)
        {
            case BroadsideVolley:
            {
                // The deck that will fire: its whole lane, bright (grey if that side is still reloading).
                var side = BroadsideVolley.SideToward(ship, aim.Cursor);
                DrawFiringLane(ship, side, pos, ship.Heading, ready ? AimFill * 1.6f : AimCooling);
                break;
            }
            case LongGun:
            {
                var direction = LongGun.AimDirection(ship, aim.Cursor);
                var side = new NVector2(-direction.Y, direction.X) * (LongGun.ShotRadius + 0.15f);
                var start = pos + direction * (ship.Stats.Beam / 2f);
                var end = pos + direction * LongGun.RangeFor(ship);
                Span<Vector2> path = stackalloc Vector2[]
                {
                    IsoProjection.WorldToIso(start + side), IsoProjection.WorldToIso(end + side),
                    IsoProjection.WorldToIso(end - side), IsoProjection.WorldToIso(start - side),
                };
                _batch.FillConvex(path, ready ? AimFill : AimCooling);
                _batch.Outline(path, ready ? AimEdge : AimCooling);

                Span<Vector2> head = stackalloc Vector2[]
                {
                    IsoProjection.WorldToIso(end + direction * 0.8f),
                    IsoProjection.WorldToIso(end + side * 2.2f),
                    IsoProjection.WorldToIso(end - side * 2.2f),
                };
                _batch.FillConvex(head, ready ? AimEdge : AimCooling);
                break;
            }
            case Mortar:
            {
                DrawGroundCircle(pos, Mortar.RangeFor(ship), ready ? AimEdge * 0.5f : AimCooling);
                var landing = Mortar.LandingPoint(ship, aim.Cursor) + (pos - ship.Position);
                FillGroundCircle(landing, Mortar.BlastRadius, ready ? AimFill : AimCooling);
                DrawGroundCircle(landing, Mortar.BlastRadius, ready ? AimEdge : AimCooling);
                break;
            }
        }
    }

    private void FillGroundCircle(NVector2 center, float radius, Color color)
    {
        if (radius <= 0.01f)
            return;
        Span<Vector2> points = stackalloc Vector2[32];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = MathF.Tau * i / points.Length;
            points[i] = IsoProjection.WorldToIso(center + new NVector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }
        _batch.FillConvex(points, color);
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
        && attacker.Abilities.Any(a => a?.Definition is BroadsideVolley)
        && BroadsideVolley.SideCovering(attacker, target.Position, target.Stats.Radius) != BroadsideSide.None;
}
