using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Screen-space markers pinned to the map: a coin over unplundered islands near the player, and a progress bar over
/// the player's ship while plundering or weighing anchor.
/// Trade shows here too: a crate over each island our cargo is bound for, and what each floating crate holds. Each
/// fortress is named over its keep, with its level while it's held.
/// </summary>
public sealed class IslandOverlays
{
    // Show "ripe for plunder" coins on islands within this many tiles of the player's ship.
    private const float CoinMarkerRange = 15f;

    private static readonly Vector2 DigitSize = new(8f, 14f);
    private const float DigitSpacing = 3f;
    private const float DigitThickness = 2f;

    private static readonly Color Panel = new Color(12, 16, 24) * 0.8f;
    private static readonly Color TimerDigits = new(235, 235, 240);
    private static readonly Color Coin = new(235, 190, 60);
    private static readonly Color CoinRim = new(150, 105, 25);
    private static readonly Color ProgressBack = new Color(0, 0, 0) * 0.6f;
    private static readonly Color ProgressFill = new(235, 190, 60);
    private static readonly Color AnchorFill = new(170, 220, 255);
    private static readonly Color FortressHeld = new(240, 110, 90);
    private static readonly Color FortressTaken = new(140, 230, 150);

    private readonly PrimitiveBatch _batch;

    public IslandOverlays(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    /// <param name="anchorDropProgress">0..1 while the player is holding X to drop anchor.</param>
    public void Draw(World world, Ship? localShip, float alpha, Matrix view, HudView hud, float anchorDropProgress = 0f)
    {
        _batch.Begin(hud.Transform);
        var bounds = hud.Viewport.Bounds;
        bounds.Inflate(60, 60);

        foreach (var island in world.Islands)
        {
            var screen = hud.FromScreen(Vector2.Transform(IsoProjection.WorldToIso(island.Center) - new Vector2(0, IslandScenery.MarkerHeight(island)), view));
            if (!bounds.Contains(screen.ToPoint()))
                continue;

            if (island.IsFortress)
            {
                var held = world.IsHeld(island);
                var label = held ? $"{island.Name}  LV {island.Level}  {CardRewards.RewardLabel(island.Level)}" : $"{island.Name}  PORT";
                DrawLabel(label, screen - new Vector2(0f, 30f), held ? FortressHeld : FortressTaken);
                if (held)
                    continue; // no plundering it yet
            }

            if (!world.IsPlundered(island) && localShip is not null && island.DistanceTo(localShip.Position) <= CoinMarkerRange)
                DrawCoin(screen, 8f);
        }

        // Destinations of our cargo, lifted clear of any plunder marker on the same island.
        if (localShip is not null)
        {
            foreach (var lot in localShip.Cargo)
            {
                if (world.FindIsland(lot.Contract.DestinationIslandId) is not { } destination)
                    continue;
                var screen = hud.FromScreen(Vector2.Transform(IsoProjection.WorldToIso(destination.Center) - new Vector2(0, IslandScenery.MarkerHeight(destination)), view));
                if (bounds.Contains(screen.ToPoint()))
                    TradeMarkers.DrawCrate(_batch, screen - new Vector2(0f, 26f), 16f, TradeMarkers.Cargo);
            }
        }

        // Floating cargo: how many units, and whether there's room for it aboard.
        foreach (var crate in world.Trade.Crates)
        {
            var screen = hud.FromScreen(Vector2.Transform(IsoProjection.WorldToIso(crate.Position), view));
            if (!bounds.Contains(screen.ToPoint()))
                continue;
            var fits = localShip is null || localShip.FreeCargo >= crate.Cargo.RemainingUnits;
            DrawCrateCount(screen - new Vector2(0f, 26f), crate.Cargo.RemainingUnits, fits);
        }

        if (localShip is not null)
        {
            var position = NVector2.Lerp(localShip.PreviousPosition, localShip.Position, alpha);
            var above = hud.FromScreen(Vector2.Transform(IsoProjection.WorldToIso(position) - new Vector2(0, ShipVisuals.HealthHeight + 14f), view));

            // Raising cancels plundering, and dropping only happens under way, so at most one of these shows.
            if (anchorDropProgress > 0f && localShip.Anchor == AnchorState.Weighed)
            {
                DrawProgressBar(above, anchorDropProgress, AnchorFill);
                DrawAnchorMark(above + new Vector2(-29f, 0f));
            }
            else if (localShip.Anchor == AnchorState.Raising)
            {
                DrawProgressBar(above, Anchoring.RaiseProgress(localShip), AnchorFill);
                DrawAnchorMark(above + new Vector2(-29f, 0f));
            }
            else if (localShip.PlunderIslandId is not null)
            {
                DrawProgressBar(above, Plundering.Progress(localShip), ProgressFill);
                DrawCoin(above + new Vector2(-31f, 0f), 5f);
            }
        }

        _batch.Flush();
    }

    /// <summary>Units in a floating crate, dimmed when it won't fit in our hold.</summary>
    private void DrawCrateCount(Vector2 center, int units, bool fits)
    {
        var text = units.ToString();
        var width = SegmentDigits.Measure(text, DigitSize, DigitSpacing);
        var top = center.Y - DigitSize.Y / 2f;
        FillRect(new Vector2(center.X - width / 2f - 6f, top - 5f), new Vector2(width + 12f, DigitSize.Y + 10f), Panel);
        SegmentDigits.Draw(_batch, text, new Vector2(center.X - width / 2f, top), DigitSize, DigitSpacing, DigitThickness,
            fits ? TradeMarkers.Cargo : TimerDigits * 0.5f);
    }

    private void DrawCoin(Vector2 center, float radius)
    {
        Span<Vector2> coin = stackalloc Vector2[12];
        for (var i = 0; i < coin.Length; i++)
        {
            var angle = MathF.Tau * i / coin.Length;
            coin[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
        _batch.FillConvex(coin, Coin);
        _batch.Outline(coin, CoinRim);
    }

    private void DrawProgressBar(Vector2 center, float progress, Color fill)
    {
        const float width = 44f;
        const float height = 6f;
        var topLeft = center - new Vector2(width / 2f, height / 2f);
        FillRect(topLeft, new Vector2(width, height), ProgressBack);
        FillRect(topLeft + Vector2.One, new Vector2((width - 2f) * Math.Clamp(progress, 0f, 1f), height - 2f), fill);
    }

    /// <summary>A tiny anchor beside the weigh-anchor bar.</summary>
    private void DrawAnchorMark(Vector2 center)
    {
        _batch.Line(center + new Vector2(0, -5), center + new Vector2(0, 5), AnchorFill);
        _batch.Line(center + new Vector2(-3, -3), center + new Vector2(3, -3), AnchorFill);
        _batch.Line(center + new Vector2(-5, 2), center + new Vector2(0, 5), AnchorFill);
        _batch.Line(center + new Vector2(0, 5), center + new Vector2(5, 2), AnchorFill);
    }

    /// <summary>Text centered on <paramref name="center"/>, on a dark panel.</summary>
    private void DrawLabel(string text, Vector2 center, Color color)
    {
        const float scale = 1.5f;
        var width = PixelFont.Measure(text, scale);
        var height = PixelFont.Height(scale);
        FillRect(center - new Vector2(width / 2f + 5f, height / 2f + 4f), new Vector2(width + 10f, height + 8f), Panel);
        PixelFont.Draw(_batch, text, center - new Vector2(width / 2f, height / 2f), scale, color);
    }

    private void FillRect(Vector2 topLeft, Vector2 size, Color color)
    {
        Span<Vector2> rect = stackalloc Vector2[] { topLeft, topLeft + new Vector2(size.X, 0), topLeft + size, topLeft + new Vector2(0, size.Y) };
        _batch.FillConvex(rect, color);
    }
}
