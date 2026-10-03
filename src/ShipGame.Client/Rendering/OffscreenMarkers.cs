using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Trading;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Arrows pinned to the screen edge pointing at pirates out of view: bright when one is after you, dim while it
/// guards its spot. On a big map, the only way to find a wave until there's a minimap. Islands our cargo is bound for
/// get a gold arrow too, so a delivery can be steered for without opening the map.
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

    public void Draw(World world, Ship? localShip, float alpha, Matrix view, HudView hud)
    {
        _batch.Begin(hud.Transform);
        foreach (var ship in world.Ships)
        {
            if (ship.Team != Team.Pirates)
                continue;
            var hostile = ship.Stance != NpcStance.Guarding;
            DrawArrow(NVector2.Lerp(ship.PreviousPosition, ship.Position, alpha), hostile ? Hostile : Dormant, view, hud);
        }

        foreach (var lot in localShip?.Cargo ?? (IReadOnlyList<CargoLot>)Array.Empty<CargoLot>())
        {
            if (world.FindIsland(lot.Contract.DestinationIslandId) is { } destination)
                DrawArrow(destination.Center, TradeMarkers.Cargo, view, hud);
        }
        _batch.Flush();
    }

    /// <summary>An arrow on the screen edge toward <paramref name="target"/>; nothing if it's in view.</summary>
    private void DrawArrow(NVector2 target, Color color, Matrix view, HudView hud)
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
        var baseCenter = tip - forward * ArrowLength;
        Span<Vector2> arrow = stackalloc Vector2[] { tip, baseCenter + side * ArrowHalfWidth, baseCenter - side * ArrowHalfWidth };
        _batch.FillConvex(arrow, color);
    }
}
