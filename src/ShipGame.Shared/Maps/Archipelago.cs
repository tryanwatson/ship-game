using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Maps;

/// <summary>
/// The hand-placed island layout for the 192x192 map. Islands are built from a few hand-drawn convex outlines,
/// each rotated and scaled to an exact area. The middle (where players start) is kept clear. A few islands, spread
/// around the start, have shipyards.
/// </summary>
public static class Archipelago
{
    public static readonly Vector2 Size = new(192f, 192f);

    // Hand-drawn outlines (any scale; Island.FromTemplate sizes them). All convex.
    private static readonly Vector2[] Long = { new(3, 0), new(2, 1.2f), new(-1, 1.4f), new(-3, 0.6f), new(-3.2f, -0.4f), new(-1, -1.3f), new(2, -1.1f) };
    private static readonly Vector2[] Round = { new(2, 0), new(1.5f, 1.4f), new(0, 2.1f), new(-1.6f, 1.3f), new(-2.1f, -0.2f), new(-1.2f, -1.7f), new(0.4f, -2), new(1.7f, -1.1f) };
    private static readonly Vector2[] Wedge = { new(2.5f, -0.5f), new(1.8f, 1.5f), new(-0.5f, 2), new(-2.2f, 0.8f), new(-1.8f, -1.4f), new(0.5f, -1.8f) };
    private static readonly Vector2[] Bean = { new(2.4f, 0.3f), new(1.2f, 1.6f), new(-1.4f, 1.5f), new(-2.6f, 0.2f), new(-1.6f, -1.2f), new(1, -1.4f) };

    private static readonly (Vector2[] Template, Vector2 Center, float Area, float Rotation, bool Shipyard)[] Layout =
    {
        (Long, new(60, 70), 80, 0.3f, true),
        (Round, new(130, 62), 55, 0f, false),
        (Wedge, new(150, 122), 95, 1.0f, false),
        (Bean, new(70, 140), 40, 2.0f, false),
        (Round, new(100, 38), 65, 0.7f, false),
        (Long, new(38, 102), 90, 1.4f, false),
        (Wedge, new(112, 152), 35, 0.2f, true),
        (Bean, new(160, 40), 70, 0.5f, false),
        (Round, new(30, 30), 50, 0f, false),
        (Long, new(165, 170), 85, 2.5f, false),
        (Wedge, new(30, 165), 60, 1.8f, false),
        (Bean, new(126, 100), 34, 0.9f, true),
    };

    public static IReadOnlyList<Island> CreateIslands() =>
        Layout.Select((island, i) => Island.FromTemplate(i + 1, island.Template, island.Center, island.Area, island.Rotation, island.Shipyard)).ToList();
}
