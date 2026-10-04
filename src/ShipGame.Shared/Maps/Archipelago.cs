using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Maps;

/// <summary>One band of the map, running its full width: everything in it shares its level.</summary>
/// <param name="North">World Y of its northern edge (north is -Y).</param>
/// <param name="South">World Y of its southern edge.</param>
public sealed record Sea(string Name, int Level, float North, float South)
{
    public bool Contains(float y) => y >= North && y < South;
}

/// <summary>A knot of pirates riding at anchor, guarding their patch of sea.</summary>
/// <param name="Count">Pirates for a crew of one; bigger crews meet more (see <c>PirateCamps</c>).</param>
public sealed record PirateCamp(Vector2 Position, int Count, int Level);

/// <summary>
/// The hand-placed map: a long run of seas from the start, at the southern edge, to the pirate flagship at the far
/// north. Each sea is more dangerous than the last, and pays more: its pirates are higher level and its islands
/// richer. Every sea but the last has a shipyard (which doubles as a trading post). Islands are built from a few
/// hand-drawn convex outlines, each rotated and scaled to an exact area.
/// </summary>
public static class Archipelago
{
    public static readonly Vector2 Size = new(96f, 900f);

    /// <summary>Where the crew starts: near the southern edge, in the middle, with clear water all round.</summary>
    public static readonly Vector2 Start = new(48f, 880f);

    /// <summary>Facing north, up the map.</summary>
    public const float StartHeading = -MathF.PI / 2f;

    /// <summary>The flagship: sinking it wins the run.</summary>
    public static readonly Vector2 BossPosition = new(48f, 26f);

    public const int BossLevel = 8;

    /// <summary>From the start, northward. They tile the map's full height.</summary>
    public static readonly IReadOnlyList<Sea> Seas = new Sea[]
    {
        new("THE SHALLOWS", 1, 760f, 900f),
        new("GULL REACH", 2, 620f, 760f),
        new("BRIMSTONE STRAIT", 3, 480f, 620f),
        new("THE GREYWATER", 4, 340f, 480f),
        new("SKULL SOUND", 5, 200f, 340f),
        new("WRECKERS DEEP", 6, 90f, 200f),
        new("THE DREAD WATERS", 7, 0f, 90f),
    };

    /// <summary>The sea at <paramref name="position"/>; positions off either end of the map count as the nearest sea.</summary>
    public static Sea SeaAt(Vector2 position)
    {
        foreach (var sea in Seas)
        {
            if (sea.Contains(position.Y))
                return sea;
        }
        return position.Y < 0f ? Seas[^1] : Seas[0];
    }

    public static int LevelAt(Vector2 position) => SeaAt(position).Level;

    // Hand-drawn outlines (any scale; Island.FromTemplate sizes them). All convex.
    private static readonly Vector2[] Long = { new(3, 0), new(2, 1.2f), new(-1, 1.4f), new(-3, 0.6f), new(-3.2f, -0.4f), new(-1, -1.3f), new(2, -1.1f) };
    private static readonly Vector2[] Round = { new(2, 0), new(1.5f, 1.4f), new(0, 2.1f), new(-1.6f, 1.3f), new(-2.1f, -0.2f), new(-1.2f, -1.7f), new(0.4f, -2), new(1.7f, -1.1f) };
    private static readonly Vector2[] Wedge = { new(2.5f, -0.5f), new(1.8f, 1.5f), new(-0.5f, 2), new(-2.2f, 0.8f), new(-1.8f, -1.4f), new(0.5f, -1.8f) };
    private static readonly Vector2[] Bean = { new(2.4f, 0.3f), new(1.2f, 1.6f), new(-1.4f, 1.5f), new(-2.6f, 0.2f), new(-1.6f, -1.2f), new(1, -1.4f) };

    private static readonly (Vector2[] Template, Vector2 Center, float Area, float Rotation, bool Shipyard, string Name)[] Layout =
    {
        // The Shallows
        (Round, new(22, 842), 45, 0f, false, "COVE END"),
        (Bean, new(75, 826), 40, 0.5f, false, "GULL ROCK"),
        (Long, new(40, 790), 70, 0.3f, true, "SALT KEY"),
        (Wedge, new(78, 772), 35, 0.2f, false, "LITTLE CAY"),
        // Gull Reach
        (Bean, new(22, 728), 55, 2.0f, false, "MANGROVE"),
        (Round, new(68, 712), 60, 0.7f, false, "WINDWARD"),
        (Wedge, new(34, 668), 50, 1.8f, true, "PORT MERROW"),
        (Long, new(74, 642), 70, 1.4f, false, "LONGREACH"),
        // Brimstone Strait
        (Wedge, new(20, 592), 95, 1.0f, false, "BRIMSTONE"),
        (Long, new(77, 584), 85, 2.2f, false, "CINDER ISLE"),
        (Bean, new(52, 540), 45, 0.9f, true, "HAVEN"),
        (Round, new(22, 502), 50, 0f, false, "OLD FORT"),
        (Bean, new(78, 498), 40, 0.5f, false, "SKERRY"),
        // The Greywater
        (Long, new(32, 458), 80, 0.3f, false, "WRECK POINT"),
        (Round, new(73, 438), 65, 0.4f, false, "GREY TOR"),
        (Wedge, new(24, 398), 55, 1.2f, true, "FARHOLD"),
        (Bean, new(67, 378), 70, 2.4f, false, "THE TEETH"),
        (Round, new(40, 350), 35, 0f, false, "LANTERN ROCK"),
        // Skull Sound
        (Bean, new(22, 318), 60, 0.8f, false, "SKULL ISLE"),
        (Wedge, new(71, 308), 80, 2.0f, false, "BONEYARD"),
        (Long, new(48, 266), 60, 1.5f, true, "BLACKWATER"),
        (Round, new(20, 228), 55, 0.3f, false, "GALLOWS KEY"),
        (Long, new(76, 220), 75, 2.5f, false, "DEAD MANS REST"),
        // Wreckers Deep
        (Wedge, new(30, 180), 70, 0.6f, false, "WRECKERS REEF"),
        (Round, new(74, 168), 60, 0f, false, "STORM CAY"),
        (Bean, new(50, 138), 45, 1.2f, true, "LAST LIGHT"),
        (Long, new(20, 108), 65, 1.9f, false, "SHIVER ISLE"),
        (Bean, new(78, 104), 55, 0.4f, false, "IRONSIDE"),
        // The Dread Waters
        (Round, new(20, 44), 80, 0.2f, false, "DREAD HOLD"),
        (Wedge, new(76, 48), 70, 1.1f, false, "THE CITADEL"),
    };

    /// <summary>For a crew of one; see <c>PirateCamps.Populate</c>. Shipyards and the start are left unguarded.</summary>
    public static readonly IReadOnlyList<PirateCamp> Camps = new PirateCamp[]
    {
        // The Shallows
        new(new(75, 812), 1, 1),
        new(new(22, 826), 1, 1),
        new(new(64, 768), 2, 1),
        // Gull Reach
        new(new(26, 744), 1, 2),
        new(new(66, 728), 2, 2),
        new(new(52, 700), 1, 2),
        new(new(70, 626), 2, 3),
        // Brimstone Strait
        new(new(48, 596), 2, 3),
        new(new(78, 566), 1, 3),
        new(new(24, 518), 2, 3),
        new(new(76, 514), 2, 4),
        // The Greywater
        new(new(52, 456), 2, 4),
        new(new(72, 420), 2, 4),
        new(new(48, 384), 2, 4),
        new(new(66, 360), 3, 5),
        // Skull Sound
        new(new(46, 318), 3, 5),
        new(new(22, 300), 2, 5),
        new(new(22, 246), 2, 5),
        new(new(74, 238), 3, 6),
        // Wreckers Deep
        new(new(52, 182), 3, 6),
        new(new(74, 152), 2, 6),
        new(new(22, 124), 3, 6),
        new(new(76, 120), 3, 7),
        // The Dread Waters: the flagship's escort
        new(new(36, 46), 2, 7),
        new(new(60, 46), 2, 7),
    };

    public static IReadOnlyList<Island> CreateIslands() =>
        Layout.Select((island, i) =>
        {
            var level = LevelAt(island.Center);
            return Island.FromTemplate(i + 1, island.Template, island.Center, island.Area, island.Rotation, island.Shipyard,
                island.Name, Progression.PirateLevels.PlunderGold(level), level);
        }).ToList();
}
