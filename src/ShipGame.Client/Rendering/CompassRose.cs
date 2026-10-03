using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Top-right compass showing where the wind blows. The rose is squashed 2:1 so it lies "on the water" like the
/// isometric map: its arrow points the same way a drifting ship visibly moves on screen.
/// </summary>
public sealed class CompassRose
{
    private const float RadiusX = 44f;
    private const float RadiusY = RadiusX / 2f;
    private const float Margin = 20f;
    private const float LabelSpace = 16f;

    private static readonly Color Face = new Color(12, 16, 24) * 0.75f;
    private static readonly Color Rim = new(90, 100, 120);
    private static readonly Color Tick = new(170, 180, 200);
    private static readonly Color NorthLabel = new(230, 230, 240);
    private static readonly Color WindArrow = new(190, 225, 255);

    // "N" as a polyline in a unit box.
    private static readonly Vector2[] GlyphN = { new(0, 1), new(0, 0), new(1, 1), new(1, 0) };

    private readonly PrimitiveBatch _batch;

    public CompassRose(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public void Draw(NVector2 wind, HudView hud)
    {
        var viewport = hud.Viewport;
        _batch.Begin(hud.Transform);
        var center = new Vector2(viewport.Width - Margin - RadiusX, Margin + LabelSpace + RadiusY);

        Span<Vector2> rim = stackalloc Vector2[32];
        for (var i = 0; i < rim.Length; i++)
            rim[i] = OnRose(center, 360f * i / rim.Length, 1f);
        _batch.FillConvex(rim, Face);
        _batch.Outline(rim, Rim);

        for (var bearing = 0; bearing < 360; bearing += 45)
        {
            var inner = bearing % 90 == 0 ? 0.7f : 0.85f;
            _batch.Line(OnRose(center, bearing, inner), OnRose(center, bearing, 1f), Tick);
        }

        var top = OnRose(center, 0f, 1f);
        var glyphSize = new Vector2(8, 10);
        var glyphOrigin = top - new Vector2(glyphSize.X / 2f, glyphSize.Y + 4f);
        for (var i = 0; i < GlyphN.Length - 1; i++)
            _batch.Line(glyphOrigin + GlyphN[i] * glyphSize, glyphOrigin + GlyphN[i + 1] * glyphSize, NorthLabel);

        if (wind.LengthSquared() > 1e-6f)
            DrawArrow(center, Compass.Bearing(wind));

        _batch.Flush();
    }

    private void DrawArrow(Vector2 center, float bearing)
    {
        var tail = OnRose(center, bearing, -0.6f);
        var neck = OnRose(center, bearing, 0.45f);
        var tip = OnRose(center, bearing, 0.85f);
        var left = OnRose(center, bearing - 90f, 0.18f) - center + neck;
        var right = OnRose(center, bearing + 90f, 0.18f) - center + neck;

        _batch.Line(tail, neck, WindArrow);
        Span<Vector2> head = stackalloc Vector2[] { tip, left, right };
        _batch.FillConvex(head, WindArrow);

        // Fletching at the tail, so the arrow reads as "blowing toward" at a glance.
        var fletchLeft = OnRose(center, bearing - 90f, 0.12f) - center + tail;
        var fletchRight = OnRose(center, bearing + 90f, 0.12f) - center + tail;
        _batch.Line(fletchLeft, fletchRight, WindArrow);
    }

    /// <summary>Point on the squashed rose at a bearing (degrees clockwise from screen-up) and fraction of its radius.</summary>
    private static Vector2 OnRose(Vector2 center, float bearing, float radius)
    {
        var radians = bearing * MathF.PI / 180f;
        return center + new Vector2(MathF.Sin(radians) * RadiusX, -MathF.Cos(radians) * RadiusY) * radius;
    }
}
