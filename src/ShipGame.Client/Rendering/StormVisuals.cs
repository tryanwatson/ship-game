using System;
using Microsoft.Xna.Framework;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>
/// The storm front rolling up from the south: everything behind its edge darkened and lashed with rain, and a
/// churning band of foam along the edge itself, so it reads at a glance where safety ends.
/// </summary>
public sealed class StormVisuals
{
    private const float EdgeFade = 6f;
    private const int EdgeBands = 6;
    private const float RainCellX = 46f;
    private const float RainCellY = 34f;

    private static readonly Color Gloom = new Color(14, 18, 30) * 0.55f;
    private static readonly Color Rain = new Color(190, 205, 230) * 0.35f;
    private static readonly Color Foam = new Color(220, 228, 240) * 0.55f;

    private readonly PrimitiveBatch _batch;

    public StormVisuals(PrimitiveBatch batch) => _batch = batch;

    /// <param name="stormY">World Y of the storm's leading edge; everything south of it is storm.</param>
    public void Draw(World world, float stormY, Matrix view, float time)
    {
        var margin = World.OutOfBoundsMargin;
        var size = world.WorldSize;
        var bottom = size.Y + margin;
        if (stormY >= bottom)
            return;

        // The visible part of the world, so rain is only made where it can be seen.
        var inverse = Matrix.Invert(view);
        var viewport = _batch.Viewport;
        var min = Vector2.Transform(Vector2.Zero, inverse);
        var max = Vector2.Transform(new Vector2(viewport.Width, viewport.Height), inverse);
        var visibleBottom = IsoProjection.IsoToWorld(max).Y;
        if (visibleBottom < stormY - EdgeFade)
            return; // the storm is all off screen to the south

        FillWorldBand(-margin, size.X + margin, stormY, bottom, Gloom);
        // Leading edge: the gloom thins out northward over a few tiles.
        for (var i = 0; i < EdgeBands; i++)
        {
            var north = stormY - EdgeFade * (i + 1) / EdgeBands;
            var south = stormY - EdgeFade * i / EdgeBands;
            FillWorldBand(-margin, size.X + margin, north, south, Gloom * (1f - (i + 0.5f) / EdgeBands) * 0.6f);
        }

        // A ragged line of foam along the edge, rolling.
        var previous = IsoProjection.WorldToIso(new NVector2(-margin, EdgeY(-margin, stormY, time)));
        for (var x = -margin + 1f; x <= size.X + margin; x += 1f)
        {
            var next = IsoProjection.WorldToIso(new NVector2(x, EdgeY(x, stormY, time)));
            _batch.Stroke(previous, next, 2f, Foam);
            previous = next;
        }

        // Rain: streaks scattered on a jittered grid in screen space, falling and slanting with the wind.
        var top = MathF.Max(min.Y, IsoProjection.WorldToIso(new NVector2(0, stormY)).Y);
        for (var row = (int)MathF.Floor(top / RainCellY); row <= (int)MathF.Ceiling(max.Y / RainCellY); row++)
        {
            for (var column = (int)MathF.Floor(min.X / RainCellX); column <= (int)MathF.Ceiling(max.X / RainCellX); column++)
            {
                var seed = Hash(column, row);
                var fall = (time * (220f + seed * 120f) + seed * 400f) % RainCellY;
                var at = new Vector2(column * RainCellX + Hash(row, column + 31) * RainCellX, row * RainCellY + fall);
                if (IsoProjection.IsoToWorld(at).Y < stormY)
                    continue;
                _batch.Stroke(at, at + new Vector2(-4f, 14f), 1f, Rain);
            }
        }
    }

    private static float EdgeY(float x, float stormY, float time) =>
        stormY + MathF.Sin(x * 0.35f + time * 1.7f) * 0.4f + MathF.Sin(x * 0.11f - time * 0.9f) * 0.6f;

    private void FillWorldBand(float left, float right, float north, float south, Color color)
    {
        Span<Vector2> corners = stackalloc Vector2[]
        {
            IsoProjection.WorldToIso(new NVector2(left, north)),
            IsoProjection.WorldToIso(new NVector2(right, north)),
            IsoProjection.WorldToIso(new NVector2(right, south)),
            IsoProjection.WorldToIso(new NVector2(left, south)),
        };
        _batch.FillConvex(corners, color);
    }

    private static float Hash(int x, int y)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
    }
}
