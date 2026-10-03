using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Arrows pinned to the screen edge pointing at pirates out of view: bright when one is after you, dim while it
/// guards its spot. On a big map, the only way to find a wave until there's a minimap.
/// </summary>
public sealed class OffscreenMarkers
{
    private const float EdgeMargin = 28f;
    private const float ArrowLength = 14f;
    private const float ArrowHalfWidth = 8f;

    private static readonly Color Hostile = new(235, 70, 55);
    private static readonly Color Dormant = new Color(200, 120, 110) * 0.55f;

    private readonly PrimitiveBatch _batch;

    public OffscreenMarkers(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public void Draw(World world, float alpha, Matrix view, Viewport viewport)
    {
        _batch.Begin(Matrix.Identity);
        var center = new Vector2(viewport.Width / 2f, viewport.Height / 2f);
        var halfExtent = center - new Vector2(EdgeMargin);
        Span<Vector2> arrow = stackalloc Vector2[3];

        foreach (var ship in world.Ships)
        {
            if (ship.Team != Team.Pirates)
                continue;

            var world2D = NVector2.Lerp(ship.PreviousPosition, ship.Position, alpha);
            var screen = Vector2.Transform(IsoProjection.WorldToIso(world2D), view);
            if (viewport.Bounds.Contains(screen.ToPoint()))
                continue;

            // Walk from the screen center toward the ship until we hit the inset border.
            var direction = screen - center;
            var scale = MathF.Min(
                direction.X == 0f ? float.MaxValue : halfExtent.X / MathF.Abs(direction.X),
                direction.Y == 0f ? float.MaxValue : halfExtent.Y / MathF.Abs(direction.Y));
            var tip = center + direction * scale;

            var forward = Vector2.Normalize(direction);
            var side = new Vector2(-forward.Y, forward.X);
            var baseCenter = tip - forward * ArrowLength;
            arrow[0] = tip;
            arrow[1] = baseCenter + side * ArrowHalfWidth;
            arrow[2] = baseCenter - side * ArrowHalfWidth;

            var hostile = ship.Stance != NpcStance.Guarding;
            _batch.FillConvex(arrow, hostile ? Hostile : Dormant);
        }

        _batch.Flush();
    }
}
