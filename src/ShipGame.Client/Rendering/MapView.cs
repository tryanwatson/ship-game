using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>
/// The full map (M): the whole square sea, drawn through the same projection as the game view, so directions on the
/// map match what you see at sea. Only what your team has discovered is filled in: discovered water is blue and
/// discovered islands appear (shipyards marked); everything else is blank parchment. The map's border is always drawn.
/// Only discovered islands are labeled: discovered fortresses show their level while held and a tick once taken. Your ship is an arrow along its heading; teammates are dots; a boss in sight is a red diamond.
/// </summary>
public sealed class MapView
{
    private const float Margin = 56f;

    private static readonly Color Backdrop = new Color(6, 8, 14) * 0.7f;
    private static readonly Color LabelBack = new Color(10, 12, 18) * 0.75f;
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
    private static readonly Color FortressHeld = new(220, 70, 55);
    private static readonly Color FortressTaken = new(120, 230, 140);
    private static readonly Color Boss = new(255, 80, 60);

    private readonly PrimitiveBatch _batch;

    public MapView(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public void Draw(World world, int localPlayerId, HudView hud)
    {
        var viewport = hud.Viewport;
        var frame = new Rectangle(0, 0, viewport.Width, viewport.Height);

        // Backdrop and labels in plain HUD space.
        _batch.Begin(hud.Transform);
        FillRect(new Vector2(frame.X, frame.Y), new Vector2(frame.Width, frame.Height), Backdrop);
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
        var scale = MathF.Min((frame.Width - 2 * Margin) / (isoRight - isoLeft), (frame.Height - 2 * Margin) / (isoBottom - isoTop));
        var center = new Vector2((isoLeft + isoRight) / 2f, (isoTop + isoBottom) / 2f);
        // Iso units to HUD units; markers and labels are drawn in HUD units so they keep a readable size.
        var toHud = Matrix.CreateTranslation(-center.X, -center.Y, 0f)
            * Matrix.CreateScale(scale, scale, 1f)
            * Matrix.CreateTranslation(frame.Center.X, frame.Center.Y + 12f, 0f);
        var mapTransform = toHud * hud.Transform;

        _batch.Begin(mapTransform);
        var localShip = world.GetPlayerShip(localPlayerId);
        var team = localShip?.Team ?? Team.Players;
        DrawCells(world, team);
        _batch.Flush();

        foreach (var island in world.Islands)
        {
            if (world.Discovery.IsDiscovered(team, island))
                DrawIsland(island, world.IsPort(island));
        }
        DrawWorldOutline(size);
        _batch.Flush();

        // Ships last, sized in screen terms so they stay visible at map scale.
        foreach (var ship in world.Ships)
        {
            if (ship.IsBoss)
            {
                DrawDiamond(IsoProjection.WorldToIso(ship.Position), 9f / scale, Boss);
                continue;
            }
            if (ship.OwnerPlayerId is null || ship.Team != team)
                continue;
            if (ship.OwnerPlayerId == localPlayerId)
                DrawShipArrow(ship, 1f / scale);
            else
                DrawDot(IsoProjection.WorldToIso(ship.Position), 4f / scale, Crew);
        }
        _batch.Flush();

        // Markers and names on top, in HUD units.
        _batch.Begin(hud.Transform);
        Vector2 ToHud(NVector2 point) => Vector2.Transform(IsoProjection.WorldToIso(point), toHud);
        DrawFortresses(world, team, ToHud);
        DrawCrewNames(world, team, localPlayerId, ToHud);
        _batch.Flush();
    }

    /// <summary>Each discovered fortress: a red badge with its level and hand while it's held, a green tick once taken.</summary>
    private void DrawFortresses(World world, Team team, Func<NVector2, Vector2> toHud)
    {
        foreach (var island in world.Islands)
        {
            if (!island.IsFortress || !world.Discovery.IsDiscovered(team, island))
                continue;
            var at = toHud(island.Center);
            if (world.IsHeld(island))
            {
                var text = $"LV {island.Level}";
                var hand = CardRewards.Hand(OfferSource.Fortress, island.Level);
                var width = PixelFont.Measure(text, 1.5f) + 8f + HandPips.Measure(hand, 1.5f);
                FillRect(at - new Vector2(width / 2f + 5f, 11f), new Vector2(width + 10f, 22f), FortressHeld);
                PixelFont.Draw(_batch, text, at - new Vector2(width / 2f, PixelFont.Height(1.5f) / 2f), 1.5f, Title);
                HandPips.Draw(_batch, hand, at + new Vector2(-width / 2f + PixelFont.Measure(text, 1.5f) + 8f, 0f), 1.5f);
            }
            else
            {
                ThickLine(at + new Vector2(-7f, 0f), at + new Vector2(-2f, 6f), 3f, FortressTaken);
                ThickLine(at + new Vector2(-2f, 6f), at + new Vector2(8f, -7f), 3f, FortressTaken);
            }
        }
    }

    /// <summary>Each crewmate's name under their marker (ours in our color).</summary>
    private void DrawCrewNames(World world, Team team, int localPlayerId, Func<NVector2, Vector2> toHud)
    {
        foreach (var ship in world.Ships)
        {
            if (ship.OwnerPlayerId is not { } owner || ship.Team != team || !world.Players.TryGetValue(owner, out var player) || player.Name.Length == 0)
                continue;
            DrawLabel(player.Name, toHud(ship.Position) + new Vector2(0f, 12f), 1.5f, owner == localPlayerId ? You : Crew);
        }
    }

    /// <summary>Text centered under <paramref name="top"/>, on a dark backing so it reads over land and sea alike.</summary>
    private void DrawLabel(string text, Vector2 top, float scale, Color color)
    {
        var width = PixelFont.Measure(text, scale);
        var height = PixelFont.Height(scale);
        FillRect(new Vector2(top.X - width / 2f - 3f, top.Y - 2f), new Vector2(width + 6f, height + 4f), LabelBack);
        PixelFont.Draw(_batch, text, new Vector2(top.X - width / 2f, top.Y), scale, color);
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

    /// <param name="port">Shipyards: ships can shop there.</param>
    private void DrawIsland(Island island, bool port)
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

        if (port)
        {
            // A little roof so ports stand out at map scale.
            var c = IsoProjection.WorldToIso(island.Center);
            Span<Vector2> roof = stackalloc Vector2[] { c + new Vector2(0, -90), c + new Vector2(80, 10), c + new Vector2(-80, 10) };
            _batch.FillConvex(roof, Hut);
        }
    }

    private void DrawDiamond(Vector2 center, float radius, Color color)
    {
        Span<Vector2> points = stackalloc Vector2[]
        {
            center + new Vector2(0, -radius), center + new Vector2(radius, 0), center + new Vector2(0, radius), center + new Vector2(-radius, 0),
        };
        _batch.FillConvex(points, color);
        _batch.Outline(points, Border);
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

    /// <summary>A line <paramref name="width"/> wide, for marks that need to show up at map scale.</summary>
    private void ThickLine(Vector2 a, Vector2 b, float width, Color color)
    {
        var along = b - a;
        if (along.LengthSquared() < 1e-6f)
            return;
        along.Normalize();
        var side = new Vector2(-along.Y, along.X) * (width / 2f);
        Span<Vector2> quad = stackalloc Vector2[] { a + side, b + side, b - side, a - side };
        _batch.FillConvex(quad, color);
    }

    private void FillRect(Vector2 topLeft, Vector2 size, Color color)
    {
        Span<Vector2> rect = stackalloc Vector2[] { topLeft, topLeft + new Vector2(size.X, 0), topLeft + size, topLeft + new Vector2(0, size.Y) };
        _batch.FillConvex(rect, color);
    }
}
