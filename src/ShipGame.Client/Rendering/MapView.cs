using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Trading;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>Contracts to chart while the player picks one: <paramref name="Offers"/> in panel order, from <paramref name="Origin"/>.</summary>
public sealed record RoutePreview(Island Origin, IReadOnlyList<TradeContract> Offers, int? HighlightedContractId);

/// <summary>
/// The full map (M): the whole run of seas from south to north, drawn through the same projection as the game view,
/// so directions on the map match what you see at sea. Only what your team has discovered is filled in: discovered
/// water is blue and discovered islands appear (shipyards marked); everything else is blank parchment. The map's
/// border, the boundaries between seas (named, with their levels), and the storm are always drawn. Your ship is
/// an arrow along its heading; teammates are dots. Where the cargo in your hold is bound is always marked, and while
/// choosing a trade contract the map is drawn in an inset beside the panel with each offer's route on it.
/// </summary>
public sealed class MapView
{
    private const float Margin = 56f;
    private const float InsetMargin = 36f;

    private static readonly Color Backdrop = new Color(6, 8, 14) * 0.7f;
    private static readonly Color InsetBackdrop = new Color(14, 18, 28) * 0.92f;
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
    private static readonly Color SeaLine = new Color(70, 50, 30) * 0.6f;
    private static readonly Color SeaName = new(226, 210, 160);
    private static readonly Color Storm = new Color(20, 24, 40) * 0.7f;

    private readonly PrimitiveBatch _batch;

    public MapView(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public void Draw(World world, int localPlayerId, HudView hud, Rectangle? area = null, RoutePreview? routes = null)
    {
        var viewport = hud.Viewport;
        var frame = area ?? new Rectangle(0, 0, viewport.Width, viewport.Height);
        var margin = area is null ? Margin : InsetMargin;

        // Backdrop and labels in plain HUD space.
        _batch.Begin(hud.Transform);
        FillRect(new Vector2(frame.X, frame.Y), new Vector2(frame.Width, frame.Height), area is null ? Backdrop : InsetBackdrop);
        if (area is null)
        {
            PixelFont.Draw(_batch, "MAP", new Vector2(Margin, 18f), 3f, Title);
            const string hint = "M TO CLOSE";
            PixelFont.Draw(_batch, hint, new Vector2(viewport.Width - Margin - PixelFont.Measure(hint, 2f), 22f), 2f, Hint);
        }
        else
        {
            PixelFont.Draw(_batch, "ROUTES", new Vector2(frame.X + 12f, frame.Y + 10f), 2f, Title);
        }
        _batch.Flush();

        // The map itself: world coordinates through the iso projection, shrunk to fit and centred.
        var size = world.WorldSize;
        var isoLeft = IsoProjection.WorldToIso(new NVector2(0, size.Y)).X;
        var isoRight = IsoProjection.WorldToIso(new NVector2(size.X, 0)).X;
        var isoTop = IsoProjection.WorldToIso(NVector2.Zero).Y;
        var isoBottom = IsoProjection.WorldToIso(size).Y;
        var scale = MathF.Min((frame.Width - 2 * margin) / (isoRight - isoLeft), (frame.Height - 2 * margin) / (isoBottom - isoTop));
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
                DrawIsland(island);
        }
        foreach (var sea in Archipelago.Seas)
        {
            if (sea.South < size.Y)
                _batch.Line(IsoProjection.WorldToIso(new NVector2(0, sea.South)), IsoProjection.WorldToIso(new NVector2(size.X, sea.South)), SeaLine);
        }
        if (world.Director is { } director && director.StormY < size.Y)
            FillWorldRect(new NVector2(0, MathF.Max(0f, director.StormY)), size, Storm);
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

        // Trade on top: destinations are charted whether or not the island has been discovered yet.
        _batch.Begin(hud.Transform);
        Vector2 ToHud(NVector2 point) => Vector2.Transform(IsoProjection.WorldToIso(point), toHud);
        if (area is null)
            DrawSeaNames(size, ToHud);
        if (routes is { } preview)
            DrawRoutes(world, preview, ToHud);
        if (localShip is not null)
        {
            DrawCargoDestinations(world, localShip, ToHud);
            if (area is null)
                DrawHoldLegend(world, localShip, frame);
        }
        _batch.Flush();
    }

    /// <summary>Each sea's name and level, beside the map's eastern edge, level with the middle of the sea.</summary>
    private void DrawSeaNames(NVector2 size, Func<NVector2, Vector2> toHud)
    {
        const float scale = 1.5f;
        foreach (var sea in Archipelago.Seas)
        {
            var at = toHud(new NVector2(size.X, (sea.North + sea.South) / 2f)) + new Vector2(10f, -PixelFont.Height(scale) / 2f);
            PixelFont.Draw(_batch, $"{sea.Name} {sea.Level}", at, scale, SeaName);
        }
    }

    /// <summary>A line from the trading post to each offer's destination, lettered and colored to match the panel.</summary>
    private void DrawRoutes(World world, RoutePreview preview, Func<NVector2, Vector2> toHud)
    {
        var origin = toHud(preview.Origin.Center);
        var anyHighlighted = preview.HighlightedContractId is not null;

        // The highlighted route draws last, over the others; all lines go down before any badge so none hides a label.
        var order = new List<int>();
        for (var i = 0; i < preview.Offers.Count; i++)
            order.Add(i);
        order.Sort((x, y) => (preview.Offers[x].Id == preview.HighlightedContractId).CompareTo(preview.Offers[y].Id == preview.HighlightedContractId));

        foreach (var badges in new[] { false, true })
        {
            foreach (var i in order)
            {
                var offer = preview.Offers[i];
                if (world.FindIsland(offer.DestinationIslandId) is not { } destination)
                    continue;
                var highlighted = offer.Id == preview.HighlightedContractId;
                var color = TradeMarkers.OfferColor(i) * (anyHighlighted && !highlighted ? 0.4f : 1f);
                var to = toHud(destination.Center);
                if (!badges)
                {
                    TradeMarkers.ThickLine(_batch, origin, to, highlighted ? 3.5f : 2f, color);
                    continue;
                }
                TradeMarkers.DrawBadge(_batch, to, TradeMarkers.Letter(i), color, highlighted ? 22f : 16f, highlighted ? 2f : 1.5f);
                DrawLabel(destination.Name, to + new Vector2(0f, highlighted ? 16f : 13f), 1.5f, color);
            }
        }
        DrawDot(origin, 5f, You);
    }

    /// <summary>Where the cargo in our hold is bound: a crate on each destination, with a line from the ship.</summary>
    private void DrawCargoDestinations(World world, Ship ship, Func<NVector2, Vector2> toHud)
    {
        var from = toHud(ship.Position);
        foreach (var lot in ship.Cargo)
        {
            if (world.FindIsland(lot.Contract.DestinationIslandId) is not { } destination)
                continue;
            var to = toHud(destination.Center);
            TradeMarkers.ThickLine(_batch, from, to, 1.5f, TradeMarkers.Cargo * 0.6f);
            TradeMarkers.DrawCrate(_batch, to, 14f, TradeMarkers.Cargo);
            DrawLabel(destination.Name, to + new Vector2(0f, 12f), 1.5f, TradeMarkers.Cargo);
        }
    }

    /// <summary>Bottom-left of the full map: what's in the hold, and what each lot pays now.</summary>
    private void DrawHoldLegend(World world, Ship ship, Rectangle frame)
    {
        const float scale = 2f;
        var lineHeight = PixelFont.Height(scale) + 8f;
        var y = frame.Bottom - Margin / 2f - lineHeight * (ship.Cargo.Count + 1);
        PixelFont.Draw(_batch, $"HOLD {ship.CargoUsed}/{ship.CargoCapacity}", new Vector2(Margin, y), scale, Title);
        foreach (var lot in ship.Cargo)
        {
            y += lineHeight;
            var name = world.FindIsland(lot.Contract.DestinationIslandId)?.Name ?? "?";
            var text = $"{name}  CARGO {lot.RemainingUnits}/{lot.Contract.CargoUnits}  PAYS {lot.Payout}";
            TradeMarkers.DrawCrate(_batch, new Vector2(Margin + 6f, y + PixelFont.Height(scale) / 2f), 10f, TradeMarkers.Cargo);
            PixelFont.Draw(_batch, text, new Vector2(Margin + 20f, y), scale, TradeMarkers.Cargo);
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
