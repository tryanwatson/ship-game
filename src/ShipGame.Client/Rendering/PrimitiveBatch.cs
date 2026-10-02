using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ShipGame.Client.Rendering;

/// <summary>Batches flat-colored triangles and lines in iso space. Enough for placeholder art.</summary>
public sealed class PrimitiveBatch : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly List<VertexPositionColor> _triangles = new();
    private readonly List<VertexPositionColor> _lines = new();

    public PrimitiveBatch(GraphicsDevice device)
    {
        _device = device;
        _effect = new BasicEffect(device) { VertexColorEnabled = true };
    }

    public void Begin(Matrix view)
    {
        var viewport = _device.Viewport;
        _effect.View = view;
        _effect.Projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, 1);
        _triangles.Clear();
        _lines.Clear();
    }

    /// <summary>Fills a convex polygon as a triangle fan.</summary>
    public void FillConvex(ReadOnlySpan<Vector2> points, Color color)
    {
        for (var i = 1; i < points.Length - 1; i++)
        {
            Add(_triangles, points[0], color);
            Add(_triangles, points[i], color);
            Add(_triangles, points[i + 1], color);
        }
    }

    public void Line(Vector2 a, Vector2 b, Color color)
    {
        Add(_lines, a, color);
        Add(_lines, b, color);
    }

    public void Outline(ReadOnlySpan<Vector2> points, Color color)
    {
        for (var i = 0; i < points.Length; i++)
            Line(points[i], points[(i + 1) % points.Length], color);
    }

    /// <summary>
    /// Draws everything batched so far, triangles before lines. Call between layers that must
    /// overlap correctly (e.g. once per ship in depth order).
    /// </summary>
    public void Flush()
    {
        _device.BlendState = BlendState.AlphaBlend;
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            if (_triangles.Count > 0)
                _device.DrawUserPrimitives(PrimitiveType.TriangleList, _triangles.ToArray(), 0, _triangles.Count / 3);
            if (_lines.Count > 0)
                _device.DrawUserPrimitives(PrimitiveType.LineList, _lines.ToArray(), 0, _lines.Count / 2);
        }

        _triangles.Clear();
        _lines.Clear();
    }

    public void Dispose() => _effect.Dispose();

    private static void Add(List<VertexPositionColor> list, Vector2 p, Color color) =>
        list.Add(new VertexPositionColor(new Vector3(p, 0f), color));
}
