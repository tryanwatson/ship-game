using Microsoft.Xna.Framework;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Maps the flat 2D simulation plane onto 2:1 isometric "iso space" (pixels at zoom 1, before the camera).
/// This is purely presentational; nothing in the simulation knows about it.
/// </summary>
public static class IsoProjection
{
    public const float TileWidth = 64f;
    public const float TileHeight = 32f;

    private const float HalfWidth = TileWidth / 2f;
    private const float HalfHeight = TileHeight / 2f;

    public static Vector2 WorldToIso(NVector2 world) =>
        new((world.X - world.Y) * HalfWidth, (world.X + world.Y) * HalfHeight);

    public static NVector2 IsoToWorld(Vector2 iso)
    {
        var a = iso.X / HalfWidth;  // x - y
        var b = iso.Y / HalfHeight; // x + y
        return new NVector2((a + b) / 2f, (b - a) / 2f);
    }

    /// <summary>Draw-order key: larger values are nearer the viewer and should draw later.</summary>
    public static float Depth(NVector2 world) => world.X + world.Y;
}
