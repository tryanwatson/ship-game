using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ShipGame.Client.Rendering;

/// <summary>Top-right readouts beneath the compass: gold, then the current wave. Right-aligned icon + number panels.</summary>
public sealed class HudCounters
{
    private const float RightMargin = 20f;
    private const float FirstTop = 92f;
    private const float RowGap = 14f;
    private const float IconRadius = 9f;
    private const float IconGap = 8f;
    private const float PanelPadding = 6f;
    private const float DigitSpacing = 4f;
    private const float DigitThickness = 2.5f;

    private static readonly Vector2 DigitSize = new(11f, 20f);
    private static readonly Color Panel = new Color(12, 16, 24) * 0.75f;
    private static readonly Color Coin = new(235, 190, 60);
    private static readonly Color CoinRim = new(150, 105, 25);
    private static readonly Color GoldDigits = new(250, 225, 140);
    private static readonly Color FlagPole = new(200, 200, 210);
    private static readonly Color Flag = new(210, 70, 60);
    private static readonly Color WaveDigits = new(235, 235, 240);

    /// <summary>Where the counters end (HUD units from the top), for readouts stacked beneath them.</summary>
    public static float Bottom => FirstTop + DigitSize.Y + 2 * PanelPadding + RowGap + DigitSize.Y + PanelPadding;

    private readonly PrimitiveBatch _batch;

    public HudCounters(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public void Draw(int gold, int wave, HudView hud)
    {
        var viewport = hud.Viewport;
        _batch.Begin(hud.Transform);
        var right = viewport.Width - RightMargin;

        DrawCounter(right, FirstTop, gold, GoldDigits, DrawCoin);
        DrawCounter(right, FirstTop + DigitSize.Y + 2 * PanelPadding + RowGap, wave, WaveDigits, DrawFlag);

        _batch.Flush();
    }

    private void DrawCounter(float right, float top, int value, Color digitColor, Action<Vector2> drawIcon)
    {
        var text = Math.Max(0, value).ToString();
        var textLeft = right - SegmentDigits.Measure(text, DigitSize, DigitSpacing);
        var iconCenter = new Vector2(textLeft - IconGap - IconRadius, top + DigitSize.Y / 2f);

        var panelLeft = iconCenter.X - IconRadius - 8f;
        FillRect(new Vector2(panelLeft, top - PanelPadding), new Vector2(right + 8f - panelLeft, DigitSize.Y + 2 * PanelPadding), Panel);

        drawIcon(iconCenter);
        SegmentDigits.Draw(_batch, text, new Vector2(textLeft, top), DigitSize, DigitSpacing, DigitThickness, digitColor);
    }

    private void DrawCoin(Vector2 center)
    {
        Span<Vector2> coin = stackalloc Vector2[12];
        for (var i = 0; i < coin.Length; i++)
        {
            var angle = MathF.Tau * i / coin.Length;
            coin[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * IconRadius;
        }
        _batch.FillConvex(coin, Coin);
        _batch.Outline(coin, CoinRim);
    }

    /// <summary>A pirate pennant: pole plus a triangular flag.</summary>
    private void DrawFlag(Vector2 center)
    {
        var poleX = center.X - IconRadius * 0.6f;
        _batch.Line(new Vector2(poleX, center.Y - IconRadius), new Vector2(poleX, center.Y + IconRadius), FlagPole);
        Span<Vector2> flag = stackalloc Vector2[]
        {
            new(poleX + 1f, center.Y - IconRadius),
            new(center.X + IconRadius, center.Y - IconRadius * 0.4f),
            new(poleX + 1f, center.Y + IconRadius * 0.2f),
        };
        _batch.FillConvex(flag, Flag);
    }

    private void FillRect(Vector2 topLeft, Vector2 size, Color color)
    {
        Span<Vector2> rect = stackalloc Vector2[] { topLeft, topLeft + new Vector2(size.X, 0), topLeft + size, topLeft + new Vector2(0, size.Y) };
        _batch.FillConvex(rect, color);
    }
}
