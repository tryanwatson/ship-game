using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ShipGame.Net;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Arrows pinned to the screen edge pointing at pirates out of view: bright when one is after you, dim while it
/// guards its spot. Only pirates within <see cref="Relevance.EnterRange"/> of our ship get one (the range an online
/// server sends), not the whole map's. Where the crew has come to do something, a bigger arrow points the way: to a
/// fortress still held, or a port's shipyard.
/// </summary>
public sealed class OffscreenMarkers
{
    private const float EdgeMargin = 28f;
    private const float ArrowLength = 14f;
    private const float ArrowHalfWidth = 8f;

    private static readonly Color Hostile = new(235, 70, 55);
    private static readonly Color Dormant = new Color(200, 120, 110) * 0.55f;
    private static readonly Color Fortress = new(245, 205, 110);
    private static readonly Color Port = new(110, 180, 240);

    private readonly PrimitiveBatch _batch;

    public OffscreenMarkers(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public void Draw(World world, Ship? localShip, float alpha, Matrix view, HudView hud)
    {
        _batch.Begin(hud.Transform);
        if (localShip is not null)
        {
            foreach (var island in world.Islands)
            {
                if (world.IsHeld(island))
                    DrawArrow(island.Center, Fortress, view, hud, 1.5f);
                else if (island.HasShipyard)
                    DrawArrow(island.Center, Port, view, hud, 1.5f);
            }
        }
        foreach (var ship in world.Ships)
        {
            if (ship.Team != Team.Pirates || localShip is null
                || NVector2.Distance(ship.Position, localShip.Position) > Relevance.EnterRange)
                continue;
            var hostile = ship.Stance != NpcStance.Patrolling;
            DrawArrow(NVector2.Lerp(ship.PreviousPosition, ship.Position, alpha), hostile ? Hostile : Dormant, view, hud);
        }
        _batch.Flush();
    }

    /// <summary>An arrow on the screen edge toward <paramref name="target"/>; nothing if it's in view.</summary>
    private void DrawArrow(NVector2 target, Color color, Matrix view, HudView hud, float size = 1f)
    {
        var viewport = hud.Viewport;
        var screen = hud.FromScreen(Vector2.Transform(IsoProjection.WorldToIso(target), view));
        if (viewport.Bounds.Contains(screen.ToPoint()))
            return;

        // Walk from the screen center toward the target until we hit the inset border.
        var center = new Vector2(viewport.Width / 2f, viewport.Height / 2f);
        var halfExtent = center - new Vector2(EdgeMargin);
        var direction = screen - center;
        var scale = MathF.Min(
            direction.X == 0f ? float.MaxValue : halfExtent.X / MathF.Abs(direction.X),
            direction.Y == 0f ? float.MaxValue : halfExtent.Y / MathF.Abs(direction.Y));
        var tip = center + direction * scale;

        var forward = Vector2.Normalize(direction);
        var side = new Vector2(-forward.Y, forward.X);
        var baseCenter = tip - forward * ArrowLength * size;
        Span<Vector2> arrow = stackalloc Vector2[] { tip, baseCenter + side * ArrowHalfWidth * size, baseCenter - side * ArrowHalfWidth * size };
        _batch.FillConvex(arrow, color);
    }
}
