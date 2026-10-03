using System;
using Microsoft.Xna.Framework;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>
/// The look of trade on the HUD and map, shared so a contract reads the same everywhere: each offer at a trading
/// post gets a letter and a color (on the panel and on its destination marker), and cargo in the hold is a crate.
/// </summary>
public static class TradeMarkers
{
    public static readonly Color Cargo = new(240, 200, 90);
    public static readonly Color CargoEdge = new(110, 75, 30);
    private static readonly Color[] Offers = { new(255, 140, 70), new(90, 210, 230), new(225, 120, 235) };
    private static readonly Color BadgeText = new(20, 20, 26);

    private static readonly string[] Points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    public static Color OfferColor(int index) => Offers[index % Offers.Length];

    public static string Letter(int index) => ((char)('A' + index % 26)).ToString();

    /// <summary>The eight-point compass direction from one place to another, as shown on screen.</summary>
    public static string BearingName(NVector2 from, NVector2 to)
    {
        var bearing = Compass.Bearing(to - from);
        return Points[(int)MathF.Round(bearing / 45f) % Points.Length];
    }

    /// <summary>A lettered square badge centered on <paramref name="center"/>, <paramref name="size"/> across.</summary>
    public static void DrawBadge(PrimitiveBatch batch, Vector2 center, string letter, Color color, float size, float textScale)
    {
        FillSquare(batch, center, size / 2f, color);
        OutlineSquare(batch, center, size / 2f, BadgeText);
        var text = new Vector2(PixelFont.Measure(letter, textScale), PixelFont.Height(textScale));
        PixelFont.Draw(batch, letter, center - text / 2f, textScale, BadgeText);
    }

    /// <summary>A strapped cargo crate centered on <paramref name="center"/>, <paramref name="size"/> across.</summary>
    public static void DrawCrate(PrimitiveBatch batch, Vector2 center, float size, Color fill)
    {
        var half = size / 2f;
        FillSquare(batch, center, half, fill);
        OutlineSquare(batch, center, half, CargoEdge);
        batch.Line(center + new Vector2(-half, -half), center + new Vector2(half, half), CargoEdge);
        batch.Line(center + new Vector2(half, -half), center + new Vector2(-half, half), CargoEdge);
    }

    /// <summary>A line <paramref name="width"/> wide, for routes that need to show up at map scale.</summary>
    public static void ThickLine(PrimitiveBatch batch, Vector2 a, Vector2 b, float width, Color color)
    {
        var along = b - a;
        if (along.LengthSquared() < 1e-6f)
            return;
        along.Normalize();
        var side = new Vector2(-along.Y, along.X) * (width / 2f);
        Span<Vector2> quad = stackalloc Vector2[] { a + side, b + side, b - side, a - side };
        batch.FillConvex(quad, color);
    }

    private static void FillSquare(PrimitiveBatch batch, Vector2 center, float half, Color color)
    {
        Span<Vector2> corners = stackalloc Vector2[]
        {
            center + new Vector2(-half, -half), center + new Vector2(half, -half),
            center + new Vector2(half, half), center + new Vector2(-half, half),
        };
        batch.FillConvex(corners, color);
    }

    private static void OutlineSquare(PrimitiveBatch batch, Vector2 center, float half, Color color)
    {
        Span<Vector2> corners = stackalloc Vector2[]
        {
            center + new Vector2(-half, -half), center + new Vector2(half, -half),
            center + new Vector2(half, half), center + new Vector2(-half, half),
        };
        batch.Outline(corners, color);
    }
}
