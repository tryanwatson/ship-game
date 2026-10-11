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
/// Each fortress is named over its keep, with its level while it's held.
/// </summary>
public sealed class IslandOverlays
{
    // Show "ripe for plunder" coins on islands within this many tiles of the player's ship.
    private const float CoinMarkerRange = 15f;

    private static readonly Color Panel = new Color(12, 16, 24) * 0.8f;
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
                if (world.IsHeld(island))
                {
                    DrawLabel(island.Name, screen - new Vector2(0f, 52f), FortressHeld);
                    DrawHand(island.Level, screen - new Vector2(0f, 30f));
                    continue; // no plundering it yet
                }
                DrawLabel($"{island.Name}  TAKEN", screen - new Vector2(0f, 30f), FortressTaken);
            }

            if (!world.IsPlundered(island) && localShip is not null && island.DistanceTo(localShip.Position) <= CoinMarkerRange)
                DrawCoin(screen, 8f);
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

    /// <summary>Under a held fortress's name: its level, and the hand taking it deals.</summary>
    private void DrawHand(int level, Vector2 center)
    {
        const float scale = 1.5f;
        var text = $"LV {level}";
        var hand = CardRewards.Hand(OfferSource.Fortress, level);
        var width = PixelFont.Measure(text, scale) + 8f + HandPips.Measure(hand, scale);
        var height = HandPips.PipHeight(scale);
        FillRect(center - new Vector2(width / 2f + 5f, height / 2f + 4f), new Vector2(width + 10f, height + 8f), Panel);
        PixelFont.Draw(_batch, text, center - new Vector2(width / 2f, PixelFont.Height(scale) / 2f), scale, FortressHeld);
        HandPips.Draw(_batch, hand, center + new Vector2(-width / 2f + PixelFont.Measure(text, scale) + 8f, 0f), scale);
    }

    private void FillRect(Vector2 topLeft, Vector2 size, Color color)
    {
        Span<Vector2> rect = stackalloc Vector2[] { topLeft, topLeft + new Vector2(size.X, 0), topLeft + size, topLeft + new Vector2(0, size.Y) };
        _batch.FillConvex(rect, color);
    }
}
