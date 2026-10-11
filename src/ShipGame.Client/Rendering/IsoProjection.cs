using Microsoft.Xna.Framework;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Maps the flat 2D simulation plane onto 2:1 isometric "iso space" (pixels at zoom 1, before the camera).
/// The simulation's axes are screen-aligned (north, world -Y, is straight up the screen; east, +X, is right), so the
/// region of sea is a plain rectangle; the isometric tile grid runs diagonally across them, at 45 degrees.
/// Scenery built on that grid uses <see cref="Grid"/>. This is purely presentational; nothing in the simulation knows
/// about it.
/// </summary>
public static class IsoProjection
{
    public const float TileWidth = 64f;
    public const float TileHeight = 32f;

    private const float Sqrt2 = 1.41421356f;

    // One world unit across the screen, and one up it (squashed 2:1, like the tiles).
    private const float ScaleX = TileWidth / 2f * Sqrt2;
    private const float ScaleY = TileHeight / 2f * Sqrt2;

    public static Vector2 WorldToIso(NVector2 world) => new(world.X * ScaleX, world.Y * ScaleY);

    public static NVector2 IsoToWorld(Vector2 iso) => new(iso.X / ScaleX, iso.Y / ScaleY);

    /// <summary>
    /// A world offset from one measured along the isometric tile grid: +x runs down-right on screen, +y down-left.
    /// Boxes built on it (huts, crates) draw as the classic iso diamonds.
    /// </summary>
    public static NVector2 Grid(float x, float y) => new((x - y) / Sqrt2, (x + y) / Sqrt2);

    /// <summary>Draw-order key: larger values are nearer the viewer and should draw later.</summary>
    public static float Depth(NVector2 world) => world.Y;
}
