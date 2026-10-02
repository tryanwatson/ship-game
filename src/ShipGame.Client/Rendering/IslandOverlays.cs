using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Screen-space markers pinned to the map: a countdown over each plundered island until it's ripe again, a coin
/// over ripe islands near the player, and a progress bar over the player's ship while plundering or weighing anchor.
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
    private static readonly Color Hourglass = new(200, 170, 120);
    private static readonly Color Coin = new(235, 190, 60);
    private static readonly Color CoinRim = new(150, 105, 25);
    private static readonly Color ProgressBack = new Color(0, 0, 0) * 0.6f;
    private static readonly Color ProgressFill = new(235, 190, 60);
    private static readonly Color AnchorFill = new(170, 220, 255);

    private readonly PrimitiveBatch _batch;

    public IslandOverlays(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public void Draw(World world, Ship? localShip, float alpha, Matrix view, Viewport viewport)
    {
        _batch.Begin(Matrix.Identity);
        var bounds = viewport.Bounds;
        bounds.Inflate(60, 60);

        foreach (var island in world.Islands)
        {
            var screen = Vector2.Transform(IsoProjection.WorldToIso(island.Center), view);
            if (!bounds.Contains(screen.ToPoint()))
                continue;

            var cooldown = world.PlunderCooldownTicks(island);
            if (cooldown > 0)
                DrawCountdown(screen, (int)MathF.Ceiling(cooldown / (float)SimConstants.TickRate));
            else if (localShip is not null && island.DistanceTo(localShip.Position) <= CoinMarkerRange)
                DrawCoin(screen, 8f);
        }

        if (localShip is not null)
        {
            var position = NVector2.Lerp(localShip.PreviousPosition, localShip.Position, alpha);
            var above = Vector2.Transform(IsoProjection.WorldToIso(position) - new Vector2(0, 62f), view);

            // Raising cancels plundering, so at most one of these shows.
            if (localShip.Anchor == AnchorState.Raising)
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

    /// <summary>Hourglass + seconds remaining, centered on <paramref name="center"/>.</summary>
    private void DrawCountdown(Vector2 center, int seconds)
    {
        var text = seconds.ToString();
        var textWidth = SegmentDigits.Measure(text, DigitSize, DigitSpacing);
        const float iconWidth = 10f;
        const float gap = 6f;
        var width = iconWidth + gap + textWidth;
        var left = center.X - width / 2f;
        var top = center.Y - DigitSize.Y / 2f;

        FillRect(new Vector2(left - 6f, top - 5f), new Vector2(width + 12f, DigitSize.Y + 10f), Panel);

        // Hourglass: two triangles meeting at the waist.
        var waist = new Vector2(left + iconWidth / 2f, center.Y);
        Span<Vector2> upper = stackalloc Vector2[] { new(left, top), new(left + iconWidth, top), waist };
        Span<Vector2> lower = stackalloc Vector2[] { waist, new(left + iconWidth, top + DigitSize.Y), new(left, top + DigitSize.Y) };
        _batch.FillConvex(upper, Hourglass);
        _batch.FillConvex(lower, Hourglass);

        SegmentDigits.Draw(_batch, text, new Vector2(left + iconWidth + gap, top), DigitSize, DigitSpacing, DigitThickness, TimerDigits);
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

    private void FillRect(Vector2 topLeft, Vector2 size, Color color)
    {
        Span<Vector2> rect = stackalloc Vector2[] { topLeft, topLeft + new Vector2(size.X, 0), topLeft + size, topLeft + new Vector2(0, size.Y) };
        _batch.FillConvex(rect, color);
    }
}
