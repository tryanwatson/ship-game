using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ShipGame.Client.Rendering;

/// <summary>Batches colored geometry in world or HUD space, with explicit boundaries between overlapping layers.</summary>
public sealed class PrimitiveBatch : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly List<VertexPositionColor> _triangles = new();
    private readonly List<VertexPositionColor> _lines = new();
    private VertexPositionColor[] _triangleBuffer = Array.Empty<VertexPositionColor>();
    private VertexPositionColor[] _lineBuffer = Array.Empty<VertexPositionColor>();

    public Viewport Viewport => _device.Viewport;

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

    /// <summary>A filled stroke whose width scales with the view, unlike a hardware line.</summary>
    public void Stroke(Vector2 a, Vector2 b, float width, Color color)
    {
        var direction = b - a;
        if (direction.LengthSquared() < 1e-6f || width <= 0f)
            return;
        var side = Vector2.Normalize(new Vector2(-direction.Y, direction.X)) * (width / 2f);
        Span<Vector2> quad = stackalloc Vector2[] { a + side, b + side, b - side, a - side };
        FillConvex(quad, color);
    }

    public void FillEllipse(Vector2 center, Vector2 radius, Color color)
    {
        Span<Vector2> points = stackalloc Vector2[20];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = MathF.Tau * i / points.Length;
            points[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
        FillConvex(points, color);
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
        if (_triangles.Count == 0 && _lines.Count == 0)
            return;
        CopyVertices(_triangles, ref _triangleBuffer);
        CopyVertices(_lines, ref _lineBuffer);
        _device.BlendState = BlendState.AlphaBlend;
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            if (_triangles.Count > 0)
                _device.DrawUserPrimitives(PrimitiveType.TriangleList, _triangleBuffer, 0, _triangles.Count / 3);
            if (_lines.Count > 0)
                _device.DrawUserPrimitives(PrimitiveType.LineList, _lineBuffer, 0, _lines.Count / 2);
        }

        _triangles.Clear();
        _lines.Clear();
    }

    public void Dispose() => _effect.Dispose();

    private static void CopyVertices(List<VertexPositionColor> vertices, ref VertexPositionColor[] buffer)
    {
        if (buffer.Length < vertices.Count)
            Array.Resize(ref buffer, Math.Max(vertices.Count, Math.Max(256, buffer.Length * 2)));
        vertices.CopyTo(buffer);
    }

    private static void Add(List<VertexPositionColor> list, Vector2 p, Color color) =>
        list.Add(new VertexPositionColor(new Vector3(p, 0f), color));
}
