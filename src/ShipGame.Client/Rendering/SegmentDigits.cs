using System;
using Microsoft.Xna.Framework;

namespace ShipGame.Client.Rendering;

/// <summary>Seven-segment numbers drawn from filled bars: readable HUD numbers until the game has fonts.</summary>
public static class SegmentDigits
{
    // Segment bits: a = top, b = top-right, c = bottom-right, d = bottom, e = bottom-left, f = top-left, g = middle.
    private const int A = 1, B = 2, C = 4, D = 8, E = 16, F = 32, G = 64;

    private static readonly int[] Segments =
    {
        A | B | C | D | E | F,     // 0
        B | C,                     // 1
        A | B | G | E | D,         // 2
        A | B | G | C | D,         // 3
        F | G | B | C,             // 4
        A | F | G | C | D,         // 5
        A | F | G | E | D | C,     // 6
        A | B | C,                 // 7
        A | B | C | D | E | F | G, // 8
        A | B | C | D | F | G,     // 9
    };

    /// <summary>Width of <paramref name="text"/> when drawn with <see cref="Draw"/>.</summary>
    public static float Measure(string text, Vector2 digitSize, float spacing) =>
        text.Length == 0 ? 0f : text.Length * digitSize.X + (text.Length - 1) * spacing;

    /// <summary>Draws the digits of <paramref name="text"/> (other characters leave a gap).</summary>
    public static void Draw(PrimitiveBatch batch, string text, Vector2 topLeft, Vector2 digitSize, float spacing, float thickness, Color color)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var digit = text[i] - '0';
            if (digit is >= 0 and <= 9)
                DrawDigit(batch, Segments[digit], topLeft + new Vector2(i * (digitSize.X + spacing), 0), digitSize, thickness, color);
        }
    }

    private static void DrawDigit(PrimitiveBatch batch, int segments, Vector2 origin, Vector2 size, float t, Color color)
    {
        var w = size.X;
        var h = size.Y;
        var mid = h / 2f;

        if ((segments & A) != 0) Bar(batch, origin + new Vector2(t, 0), new Vector2(w - 2 * t, t), color);
        if ((segments & G) != 0) Bar(batch, origin + new Vector2(t, mid - t / 2f), new Vector2(w - 2 * t, t), color);
        if ((segments & D) != 0) Bar(batch, origin + new Vector2(t, h - t), new Vector2(w - 2 * t, t), color);
        if ((segments & F) != 0) Bar(batch, origin, new Vector2(t, mid), color);
        if ((segments & B) != 0) Bar(batch, origin + new Vector2(w - t, 0), new Vector2(t, mid), color);
        if ((segments & E) != 0) Bar(batch, origin + new Vector2(0, mid), new Vector2(t, h - mid), color);
        if ((segments & C) != 0) Bar(batch, origin + new Vector2(w - t, mid), new Vector2(t, h - mid), color);
    }

    private static void Bar(PrimitiveBatch batch, Vector2 topLeft, Vector2 size, Color color)
    {
        Span<Vector2> rect = stackalloc Vector2[] { topLeft, topLeft + new Vector2(size.X, 0), topLeft + size, topLeft + new Vector2(0, size.Y) };
        batch.FillConvex(rect, color);
    }
}
