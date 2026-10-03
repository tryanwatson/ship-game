using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace ShipGame.Client.Rendering;

/// <summary>
/// A built-in 5x7 pixel font (A-Z, 0-9, a few symbols) drawn as filled squares. Keeps the repo free of font
/// files, whose licenses rarely allow redistribution, until the game gets proper typography.
/// </summary>
public static class PixelFont
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;
    private const int Advance = GlyphWidth + 1;

    // Each glyph is 7 rows of 5 bits, top to bottom; bit 4 is the leftmost pixel.
    private static readonly Dictionary<char, byte[]> Glyphs = new()
    {
        ['A'] = Rows("01110", "10001", "10001", "11111", "10001", "10001", "10001"),
        ['B'] = Rows("11110", "10001", "10001", "11110", "10001", "10001", "11110"),
        ['C'] = Rows("01110", "10001", "10000", "10000", "10000", "10001", "01110"),
        ['D'] = Rows("11110", "10001", "10001", "10001", "10001", "10001", "11110"),
        ['E'] = Rows("11111", "10000", "10000", "11110", "10000", "10000", "11111"),
        ['F'] = Rows("11111", "10000", "10000", "11110", "10000", "10000", "10000"),
        ['G'] = Rows("01110", "10001", "10000", "10111", "10001", "10001", "01111"),
        ['H'] = Rows("10001", "10001", "10001", "11111", "10001", "10001", "10001"),
        ['I'] = Rows("01110", "00100", "00100", "00100", "00100", "00100", "01110"),
        ['J'] = Rows("00111", "00010", "00010", "00010", "00010", "10010", "01100"),
        ['K'] = Rows("10001", "10010", "10100", "11000", "10100", "10010", "10001"),
        ['L'] = Rows("10000", "10000", "10000", "10000", "10000", "10000", "11111"),
        ['M'] = Rows("10001", "11011", "10101", "10101", "10001", "10001", "10001"),
        ['N'] = Rows("10001", "10001", "11001", "10101", "10011", "10001", "10001"),
        ['O'] = Rows("01110", "10001", "10001", "10001", "10001", "10001", "01110"),
        ['P'] = Rows("11110", "10001", "10001", "11110", "10000", "10000", "10000"),
        ['Q'] = Rows("01110", "10001", "10001", "10001", "10101", "10010", "01101"),
        ['R'] = Rows("11110", "10001", "10001", "11110", "10100", "10010", "10001"),
        ['S'] = Rows("01111", "10000", "10000", "01110", "00001", "00001", "11110"),
        ['T'] = Rows("11111", "00100", "00100", "00100", "00100", "00100", "00100"),
        ['U'] = Rows("10001", "10001", "10001", "10001", "10001", "10001", "01110"),
        ['V'] = Rows("10001", "10001", "10001", "10001", "10001", "01010", "00100"),
        ['W'] = Rows("10001", "10001", "10001", "10101", "10101", "10101", "01010"),
        ['X'] = Rows("10001", "10001", "01010", "00100", "01010", "10001", "10001"),
        ['Y'] = Rows("10001", "10001", "01010", "00100", "00100", "00100", "00100"),
        ['Z'] = Rows("11111", "00001", "00010", "00100", "01000", "10000", "11111"),
        ['0'] = Rows("01110", "10001", "10011", "10101", "11001", "10001", "01110"),
        ['1'] = Rows("00100", "01100", "00100", "00100", "00100", "00100", "01110"),
        ['2'] = Rows("01110", "10001", "00001", "00010", "00100", "01000", "11111"),
        ['3'] = Rows("11111", "00010", "00100", "00010", "00001", "10001", "01110"),
        ['4'] = Rows("00010", "00110", "01010", "10010", "11111", "00010", "00010"),
        ['5'] = Rows("11111", "10000", "11110", "00001", "00001", "10001", "01110"),
        ['6'] = Rows("00110", "01000", "10000", "11110", "10001", "10001", "01110"),
        ['7'] = Rows("11111", "00001", "00010", "00100", "01000", "01000", "01000"),
        ['8'] = Rows("01110", "10001", "10001", "01110", "10001", "10001", "01110"),
        ['9'] = Rows("01110", "10001", "10001", "01111", "00001", "00010", "01100"),
        ['/'] = Rows("00001", "00010", "00010", "00100", "01000", "01000", "10000"),
        ['+'] = Rows("00000", "00100", "00100", "11111", "00100", "00100", "00000"),
        ['-'] = Rows("00000", "00000", "00000", "11111", "00000", "00000", "00000"),
        ['%'] = Rows("11000", "11001", "00010", "00100", "01000", "10011", "00011"),
        [':'] = Rows("00000", "01100", "01100", "00000", "01100", "01100", "00000"),
        ['.'] = Rows("00000", "00000", "00000", "00000", "00000", "01100", "01100"),
        [','] = Rows("00000", "00000", "00000", "00000", "01100", "00100", "01000"),
        ['*'] = Rows("00000", "10101", "01110", "11111", "01110", "10101", "00000"),
        ['['] = Rows("01110", "01000", "01000", "01000", "01000", "01000", "01110"),
        [']'] = Rows("01110", "00010", "00010", "00010", "00010", "00010", "01110"),
    };

    /// <summary>Pixel width of <paramref name="text"/> at <paramref name="scale"/> screen pixels per font pixel.</summary>
    public static float Measure(string text, float scale) =>
        text.Length == 0 ? 0f : (text.Length * Advance - 1) * scale;

    public static float Height(float scale) => GlyphHeight * scale;

    /// <summary>Breaks <paramref name="text"/> into lines no wider than <paramref name="maxWidth"/>, at spaces where it can.</summary>
    public static List<string> Wrap(string text, float scale, float maxWidth)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && Measure(candidate, scale) > maxWidth)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }
        if (line.Length > 0)
            lines.Add(line);
        return lines;
    }

    public static void Draw(PrimitiveBatch batch, string text, Vector2 topLeft, float scale, Color color)
    {
        Span<Vector2> pixel = stackalloc Vector2[4];
        for (var i = 0; i < text.Length; i++)
        {
            if (!Glyphs.TryGetValue(char.ToUpperInvariant(text[i]), out var rows))
                continue; // spaces and unknown characters just advance

            var origin = topLeft + new Vector2(i * Advance * scale, 0f);
            for (var row = 0; row < GlyphHeight; row++)
            {
                for (var column = 0; column < GlyphWidth; column++)
                {
                    if ((rows[row] & (1 << (GlyphWidth - 1 - column))) == 0)
                        continue;
                    var p = origin + new Vector2(column, row) * scale;
                    pixel[0] = p;
                    pixel[1] = p + new Vector2(scale, 0);
                    pixel[2] = p + new Vector2(scale, scale);
                    pixel[3] = p + new Vector2(0, scale);
                    batch.FillConvex(pixel, color);
                }
            }
        }
    }

    private static byte[] Rows(params string[] rows)
    {
        var bits = new byte[rows.Length];
        for (var i = 0; i < rows.Length; i++)
            bits[i] = Convert.ToByte(rows[i], 2);
        return bits;
    }
}
