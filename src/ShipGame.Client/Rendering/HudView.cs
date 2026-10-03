using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ShipGame.Client.Rendering;

/// <summary>
/// The HUD's coordinate space: everything over the game (bars, compass, panels, markers) is laid out as if on a
/// <see cref="Camera.ReferenceWidth"/> x <see cref="Camera.ReferenceHeight"/> screen and scaled up to the real
/// window, so it keeps its size relative to the screen and to the (also normalized) world. Uses the smaller of the
/// two ratios, so on any shape of screen the whole HUD fits; extra width or height just spreads its corners apart.
/// </summary>
public readonly struct HudView
{
    public HudView(Viewport screen)
    {
        Scale = MathF.Max(0.01f, MathF.Min(screen.Width / Camera.ReferenceWidth, screen.Height / Camera.ReferenceHeight));
        Viewport = new Viewport(0, 0, (int)MathF.Round(screen.Width / Scale), (int)MathF.Round(screen.Height / Scale));
        Transform = Matrix.CreateScale(Scale, Scale, 1f);
    }

    /// <summary>Screen pixels per HUD unit.</summary>
    public float Scale { get; }

    /// <summary>The screen's size in HUD units: lay out against this.</summary>
    public Viewport Viewport { get; }

    /// <summary>Pass to <see cref="PrimitiveBatch.Begin"/> to draw in HUD units.</summary>
    public Matrix Transform { get; }

    /// <summary>A real screen position (mouse, a projected world point) in HUD units.</summary>
    public Vector2 FromScreen(Vector2 screen) => screen / Scale;

    public Point FromScreen(Point screen) => new((int)(screen.X / Scale), (int)(screen.Y / Scale));
}
