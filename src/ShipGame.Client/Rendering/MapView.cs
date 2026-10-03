using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>
/// The full map (M). Drawn in the same isometric diamond as the game view, so directions on the map match what you
/// see at sea. Only what your team has discovered is filled in: discovered water is blue and discovered islands
/// appear (shipyards marked); everything else is blank parchment. The map's border is always drawn. Your ship is
/// an arrow along its heading; teammates are dots.
/// </summary>
public sealed class MapView
{
    private const float Margin = 56f;

    private static readonly Color Backdrop = new Color(6, 8, 14) * 0.7f;
    private static readonly Color Parchment = new(226, 210, 160);
    private static readonly Color ChartedSea = new(46, 92, 128);
    private static readonly Color Border = new(70, 50, 30);
    private static readonly Color Sand = new(214, 196, 140);
    private static readonly Color Grass = new(92, 140, 70);
    private static readonly Color Shore = new(120, 100, 60);
    private static readonly Color Hut = new(170, 60, 50);
    private static readonly Color You = new(255, 235, 120);
    private static readonly Color Crew = new(120, 230, 140);
    private static readonly Color Title = new(240, 220, 160);
    private static readonly Color Hint = new(190, 190, 200);

    private readonly PrimitiveBatch _batch;

    public MapView(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public void Draw(World world, int localPlayerId, HudView hud)
    {
        var viewport = hud.Viewport;

        // Backdrop and labels in plain HUD space.
        _batch.Begin(hud.Transform);
        FillRect(new Vector2(0, 0), new Vector2(viewport.Width, viewport.Height), Backdrop);
        PixelFont.Draw(_batch, "MAP", new Vector2(Margin, 18f), 3f, Title);
        const string hint = "M TO CLOSE";
        PixelFont.Draw(_batch, hint, new Vector2(viewport.Width - Margin - PixelFont.Measure(hint, 2f), 22f), 2f, Hint);
        _batch.Flush();

        // The map itself: world coordinates through the iso projection, shrunk to fit and centred.
        var size = world.WorldSize;
        var isoLeft = IsoProjection.WorldToIso(new NVector2(0, size.Y)).X;
        var isoRight = IsoProjection.WorldToIso(new NVector2(size.X, 0)).X;
        var isoTop = IsoProjection.WorldToIso(NVector2.Zero).Y;
        var isoBottom = IsoProjection.WorldToIso(size).Y;
        var scale = MathF.Min((viewport.Width - 2 * Margin) / (isoRight - isoLeft), (viewport.Height - 2 * Margin) / (isoBottom - isoTop));
        var center = new Vector2((isoLeft + isoRight) / 2f, (isoTop + isoBottom) / 2f);
        var mapTransform = Matrix.CreateTranslation(-center.X, -center.Y, 0f)
            * Matrix.CreateScale(scale, scale, 1f)
            * Matrix.CreateTranslation(viewport.Width / 2f, viewport.Height / 2f + 12f, 0f)
            * hud.Transform;

        _batch.Begin(mapTransform);
        var team = world.GetPlayerShip(localPlayerId)?.Team ?? Team.Players;
        DrawCells(world, team);
        _batch.Flush();

        foreach (var island in world.Islands)
        {
            if (world.Discovery.IsDiscovered(team, island))
                DrawIsland(island);
        }
        DrawWorldOutline(size);
        _batch.Flush();

        // Ships last, sized in screen terms so they stay visible at map scale.
        foreach (var ship in world.Ships)
        {
            if (ship.OwnerPlayerId is null || ship.Team != team)
                continue;
            if (ship.OwnerPlayerId == localPlayerId)
                DrawShipArrow(ship, 1f / scale);
            else
                DrawDot(IsoProjection.WorldToIso(ship.Position), 4f / scale, Crew);
        }
        _batch.Flush();
    }

    private void DrawCells(World world, Team team)
    {
        var discovery = world.Discovery;
        var cell = Discovery.CellSize;
        Span<Vector2> quad = stackalloc Vector2[4];
        for (var index = 0; index < discovery.CellCount; index++)
        {
            var (column, row) = discovery.CellOf(index);
            var min = new NVector2(column * cell, row * cell);
            var max = NVector2.Min(min + new NVector2(cell), world.WorldSize);
            quad[0] = IsoProjection.WorldToIso(min);
            quad[1] = IsoProjection.WorldToIso(new NVector2(max.X, min.Y));
            quad[2] = IsoProjection.WorldToIso(max);
            quad[3] = IsoProjection.WorldToIso(new NVector2(min.X, max.Y));
            _batch.FillConvex(quad, discovery.IsDiscovered(team, index) ? ChartedSea : Parchment);
        }
    }

    private void DrawIsland(Island island)
    {
        var outline = island.Outline;
        Span<Vector2> points = stackalloc Vector2[outline.Length];
        for (var i = 0; i < outline.Length; i++)
            points[i] = IsoProjection.WorldToIso(outline[i]);
        _batch.FillConvex(points, Sand);
        _batch.Outline(points, Shore);

        for (var i = 0; i < outline.Length; i++)
            points[i] = IsoProjection.WorldToIso(island.Center + (outline[i] - island.Center) * 0.7f);
        _batch.FillConvex(points, Grass);

        if (island.HasShipyard)
        {
            // A little roof so shipyards stand out at map scale.
            var c = IsoProjection.WorldToIso(island.Center);
            Span<Vector2> roof = stackalloc Vector2[] { c + new Vector2(0, -90), c + new Vector2(80, 10), c + new Vector2(-80, 10) };
            _batch.FillConvex(roof, Hut);
        }
    }

    private void DrawWorldOutline(NVector2 size)
    {
        Span<Vector2> corners = stackalloc Vector2[]
        {
            IsoProjection.WorldToIso(NVector2.Zero),
            IsoProjection.WorldToIso(new NVector2(size.X, 0)),
            IsoProjection.WorldToIso(size),
            IsoProjection.WorldToIso(new NVector2(0, size.Y)),
        };
        _batch.Outline(corners, Border);
    }

    /// <summary>An arrowhead at the ship, pointing along its heading. <paramref name="unit"/> is one screen unit in iso units.</summary>
    private void DrawShipArrow(Ship ship, float unit)
    {
        var tip = IsoProjection.WorldToIso(ship.Position + ship.Forward * 0.1f);
        var at = IsoProjection.WorldToIso(ship.Position);
        var direction = tip - at;
        direction = direction.LengthSquared() > 1e-6f ? Vector2.Normalize(direction) : new Vector2(1, 0);
        var side = new Vector2(-direction.Y, direction.X);
        Span<Vector2> arrow = stackalloc Vector2[]
        {
            at + direction * 10f * unit,
            at - direction * 6f * unit + side * 6f * unit,
            at - direction * 6f * unit - side * 6f * unit,
        };
        _batch.FillConvex(arrow, You);
        _batch.Outline(arrow, Border);
    }

    private void DrawDot(Vector2 center, float radius, Color color)
    {
        Span<Vector2> points = stackalloc Vector2[10];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = MathF.Tau * i / points.Length;
            points[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
        _batch.FillConvex(points, color);
    }

    private void FillRect(Vector2 topLeft, Vector2 size, Color color)
    {
        Span<Vector2> rect = stackalloc Vector2[] { topLeft, topLeft + new Vector2(size.X, 0), topLeft + size, topLeft + new Vector2(0, size.Y) };
        _batch.FillConvex(rect, color);
    }
}
