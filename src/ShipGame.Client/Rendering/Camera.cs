using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ShipGame.Client.Rendering;

/// <summary>2D camera over iso space.</summary>
public sealed class Camera
{
    public const float MinZoom = 0.5f;
    public const float MaxZoom = 2f;

    private float _zoom = 1f;

    /// <summary>Iso-space point at the center of the screen.</summary>
    public Vector2 Position { get; set; }

    public float Zoom
    {
        get => _zoom;
        set => _zoom = MathHelper.Clamp(value, MinZoom, MaxZoom);
    }

    public Matrix GetView(Viewport viewport) =>
        Matrix.CreateTranslation(-Position.X, -Position.Y, 0f)
        * Matrix.CreateScale(Zoom, Zoom, 1f)
        * Matrix.CreateTranslation(viewport.Width / 2f, viewport.Height / 2f, 0f);

    public Vector2 ScreenToIso(Vector2 screen, Viewport viewport) =>
        Vector2.Transform(screen, Matrix.Invert(GetView(viewport)));
}
