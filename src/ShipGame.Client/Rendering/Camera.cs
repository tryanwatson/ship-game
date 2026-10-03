using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ShipGame.Client.Rendering;

/// <summary>
/// 2D camera over iso space. The view is normalized to a reference window size, so a bigger (or differently
/// shaped) screen draws the same patch of sea larger rather than showing more of it: at a given zoom, nobody can
/// see further than a <see cref="ReferenceWidth"/> x <see cref="ReferenceHeight"/> window would. Zoom limits apply
/// on top of that, so the furthest zoom-out shows the same area on every screen.
/// </summary>
public sealed class Camera
{
    public const float MinZoom = 0.5f;
    public const float MaxZoom = 2f;

    public const float ReferenceWidth = 1280f;
    public const float ReferenceHeight = 720f;

    private float _zoom = 1f;

    /// <summary>Iso-space point at the center of the screen.</summary>
    public Vector2 Position { get; set; }

    public float Zoom
    {
        get => _zoom;
        set => _zoom = MathHelper.Clamp(value, MinZoom, MaxZoom);
    }

    /// <summary>
    /// How much this window scales the reference view. The larger of the two ratios wins, so a screen wider (or
    /// taller) than the reference shape is cropped in the other direction rather than seeing further.
    /// </summary>
    public static float ViewportScale(Viewport viewport) =>
        MathF.Max(viewport.Width / ReferenceWidth, viewport.Height / ReferenceHeight);

    /// <summary>Screen pixels per iso unit: zoom times the window's scale.</summary>
    public float EffectiveScale(Viewport viewport) => Zoom * ViewportScale(viewport);

    public Matrix GetView(Viewport viewport)
    {
        var scale = EffectiveScale(viewport);
        return Matrix.CreateTranslation(-Position.X, -Position.Y, 0f)
            * Matrix.CreateScale(scale, scale, 1f)
            * Matrix.CreateTranslation(viewport.Width / 2f, viewport.Height / 2f, 0f);
    }

    public Vector2 ScreenToIso(Vector2 screen, Viewport viewport) =>
        Vector2.Transform(screen, Matrix.Invert(GetView(viewport)));
}
