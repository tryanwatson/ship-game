using System.Numerics;

namespace ShipGame.Shared.Maps;

/// <summary>Hand-drawn island outlines, any scale (<see cref="Simulation.Island.FromTemplate"/> sizes them). All convex.</summary>
public static class IslandShapes
{
    public static readonly Vector2[] Long = { new(3, 0), new(2, 1.2f), new(-1, 1.4f), new(-3, 0.6f), new(-3.2f, -0.4f), new(-1, -1.3f), new(2, -1.1f) };
    public static readonly Vector2[] Round = { new(2, 0), new(1.5f, 1.4f), new(0, 2.1f), new(-1.6f, 1.3f), new(-2.1f, -0.2f), new(-1.2f, -1.7f), new(0.4f, -2), new(1.7f, -1.1f) };
    public static readonly Vector2[] Wedge = { new(2.5f, -0.5f), new(1.8f, 1.5f), new(-0.5f, 2), new(-2.2f, 0.8f), new(-1.8f, -1.4f), new(0.5f, -1.8f) };
    public static readonly Vector2[] Bean = { new(2.4f, 0.3f), new(1.2f, 1.6f), new(-1.4f, 1.5f), new(-2.6f, 0.2f), new(-1.6f, -1.2f), new(1, -1.4f) };

    public static readonly IReadOnlyList<Vector2[]> All = new[] { Long, Round, Wedge, Bean };
}
