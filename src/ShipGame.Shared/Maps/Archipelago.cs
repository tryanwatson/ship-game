using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Maps;

/// <summary>
/// One ring of the map, between two distances from the center (where the crew starts): everything in it shares its
/// level, so the further out, the more dangerous.
/// </summary>
/// <param name="InnerRadius">Distance from the center where it begins.</param>
/// <param name="OuterRadius">Distance from the center where the next begins (infinity for the last).</param>
public sealed record Sea(string Name, int Level, float InnerRadius, float OuterRadius)
{
    public bool Contains(float distance) => distance >= InnerRadius && distance < OuterRadius;
}

/// <summary>A pack of pirates roaming the ring of sea it starts in.</summary>
/// <param name="Count">Pirates for a crew of one; bigger crews meet more (see <c>PirateCamps</c>).</param>
public sealed record PirateCamp(Vector2 Position, int Count, int Level);

/// <summary>
/// The hand-placed map: a square sea with the crew starting in the middle. Rings of sea run outward from there, each
/// higher level than the last: its pirates are tougher and its islands richer. Islands are large and far apart. Some
/// are fortresses (see <see cref="Island.IsFortress"/>), each with its own level, defended by guns on its shore and
/// ships at sea; taking them is how a run goes on. Islands are built from a few hand-drawn convex outlines, each
/// rotated and scaled to an exact area. The positions came from a dart-throwing layout with at least 98 tiles between
/// island centers; procedural maps can replace this table later.
/// </summary>
public static class Archipelago
{
    public static readonly Vector2 Size = new(640f, 640f);

    /// <summary>The middle of the map, where the crew starts, with clear water all round.</summary>
    public static readonly Vector2 Start = Size / 2f;

    /// <summary>Facing north, up the screen.</summary>
    public const float StartHeading = -MathF.PI / 2f;

    /// <summary>From the center outward; they cover every distance.</summary>
    public static readonly IReadOnlyList<Sea> Seas = new Sea[]
    {
        new("THE SHALLOWS", 1, 0f, 125f),
        new("GULL REACH", 2, 125f, 172f),
        new("BRIMSTONE WATERS", 3, 172f, 215f),
        new("THE GREYWATER", 4, 215f, 265f),
        new("SKULL SOUND", 5, 265f, 295f),
        new("WRECKERS DEEP", 6, 295f, 335f),
        new("THE DREAD WATERS", 7, 335f, float.PositiveInfinity),
    };

    public static float DistanceFromStart(Vector2 position) => Vector2.Distance(position, Start);

    /// <summary>The ring <paramref name="position"/> is in.</summary>
    public static Sea SeaAt(Vector2 position)
    {
        var distance = DistanceFromStart(position);
        foreach (var sea in Seas)
        {
            if (sea.Contains(distance))
                return sea;
        }
        return Seas[^1];
    }

    public static int LevelAt(Vector2 position) => SeaAt(position).Level;

    // Hand-drawn outlines (any scale; Island.FromTemplate sizes them). All convex.
    private static readonly Vector2[] Long = { new(3, 0), new(2, 1.2f), new(-1, 1.4f), new(-3, 0.6f), new(-3.2f, -0.4f), new(-1, -1.3f), new(2, -1.1f) };
    private static readonly Vector2[] Round = { new(2, 0), new(1.5f, 1.4f), new(0, 2.1f), new(-1.6f, 1.3f), new(-2.1f, -0.2f), new(-1.2f, -1.7f), new(0.4f, -2), new(1.7f, -1.1f) };
    private static readonly Vector2[] Wedge = { new(2.5f, -0.5f), new(1.8f, 1.5f), new(-0.5f, 2), new(-2.2f, 0.8f), new(-1.8f, -1.4f), new(0.5f, -1.8f) };
    private static readonly Vector2[] Bean = { new(2.4f, 0.3f), new(1.2f, 1.6f), new(-1.4f, 1.5f), new(-2.6f, 0.2f), new(-1.6f, -1.2f), new(1, -1.4f) };

    private enum Kind
    {
        Plain,
        Shipyard,
        Fortress,
    }

    /// <summary>A fortress's level is its ring's unless given here (a few run a level above their waters).</summary>
    private static readonly (Vector2[] Template, Vector2 Center, float Area, float Rotation, Kind Kind, string Name, int? Level)[] Layout =
    {
        // The Shallows
        (Long, new(367, 289), 190, 0.3f, Kind.Shipyard, "SALT KEY", null),
        (Round, new(256, 327), 320, 0f, Kind.Fortress, "OLD FORT", null),
        (Bean, new(403, 401), 330, 0.5f, Kind.Fortress, "GULL FORT", null),
        // Gull Reach
        (Round, new(313, 460), 180, 0.2f, Kind.Plain, "COVE END", null),
        (Wedge, new(219, 425), 350, 1.8f, Kind.Fortress, "WINDWARD", null),
        (Long, new(184, 231), 350, 1.4f, Kind.Fortress, "FORT MERROW", null),
        (Bean, new(153, 331), 220, 2.0f, Kind.Plain, "MANGROVE", null),
        (Wedge, new(407, 175), 200, 0.7f, Kind.Shipyard, "PORT MERROW", null),
        // Brimstone Waters
        (Wedge, new(262, 157), 380, 1.0f, Kind.Fortress, "BRIMSTONE", null),
        (Long, new(494, 352), 380, 2.2f, Kind.Fortress, "CINDER ISLE", 4),
        (Long, new(501, 249), 230, 0.9f, Kind.Plain, "LONGREACH", null),
        (Bean, new(403, 503), 170, 0.5f, Kind.Plain, "SKERRY", null),
        // The Greywater
        (Round, new(497, 464), 400, 0.4f, Kind.Fortress, "GREY TOR", null),
        (Bean, new(118, 443), 400, 2.4f, Kind.Fortress, "THE TEETH", 5),
        (Bean, new(248, 547), 200, 0.9f, Kind.Shipyard, "HAVEN", null),
        (Long, new(327, 58), 220, 0.3f, Kind.Plain, "WRECK POINT", null),
        (Round, new(499, 126), 160, 0f, Kind.Plain, "LANTERN ROCK", null),
        // Skull Sound
        (Wedge, new(587, 311), 420, 2.0f, Kind.Fortress, "BONEYARD", null),
        (Long, new(348, 591), 210, 2.5f, Kind.Plain, "DEAD MANS REST", null),
        (Bean, new(58, 238), 420, 0.8f, Kind.Fortress, "SKULL ISLE", 6),
        (Round, new(48, 363), 190, 0.3f, Kind.Plain, "GALLOWS KEY", null),
        (Wedge, new(141, 107), 210, 1.2f, Kind.Shipyard, "FARHOLD", null),
        (Round, new(430, 54), 200, 0f, Kind.Plain, "STORM CAY", null),
        (Long, new(592, 429), 210, 1.9f, Kind.Plain, "SHIVER ISLE", null),
        (Bean, new(220, 45), 200, 1.2f, Kind.Plain, "PORT DUSK", null),
        // Wreckers Deep
        (Wedge, new(448, 591), 440, 0.6f, Kind.Fortress, "WRECKERS REEF", null),
        (Bean, new(594, 170), 190, 0.4f, Kind.Plain, "BLACK ROCK", null),
        (Bean, new(153, 584), 440, 0.4f, Kind.Fortress, "IRONSIDE", null),
        (Bean, new(47, 135), 210, 1.6f, Kind.Shipyard, "LAST LIGHT", null),
        // The Dread Waters
        (Round, new(59, 544), 460, 0.2f, Kind.Fortress, "DREAD HOLD", null),
        (Wedge, new(587, 549), 480, 1.1f, Kind.Fortress, "THE CITADEL", null),
        (Long, new(588, 44), 460, 0.8f, Kind.Fortress, "SKULL ROCK", 8),
    };

    /// <summary>Roaming packs, for a crew of one; see <c>PirateCamps.Populate</c>. Fortresses bring their own guards.</summary>
    public static readonly IReadOnlyList<PirateCamp> Camps = new PirateCamp[]
    {
        // The Shallows: the odd straggler, well clear of the start.
        new(new(299, 227), 1, 1),
        new(new(341, 413), 1, 1),
        // Gull Reach
        new(new(470, 280), 2, 2),
        new(new(320, 170), 1, 2),
        new(new(165, 290), 2, 2),
        // Brimstone Waters
        new(new(300, 520), 2, 3),
        new(new(442, 470), 2, 3),
        new(new(198, 170), 2, 3),
        new(new(170, 442), 2, 3),
        new(new(470, 198), 2, 3),
        // The Greywater
        new(new(230, 97), 2, 4),
        new(new(558, 353), 2, 4),
        new(new(200, 520), 2, 4),
        new(new(80, 300), 2, 4),
        // Skull Sound
        new(new(549, 159), 3, 5),
        new(new(91, 481), 2, 5),
        new(new(481, 549), 3, 5),
        new(new(380, 40), 2, 5),
        // Wreckers Deep
        new(new(109, 554), 3, 6),
        new(new(86, 109), 3, 6),
        new(new(531, 86), 3, 6),
        new(new(600, 500), 2, 6),
        // The Dread Waters
        new(new(40, 600), 3, 7),
        new(new(600, 600), 3, 7),
        new(new(520, 30), 3, 7),
    };

    /// <summary>Plundering a fallen fortress pays this many times what an ordinary island of its level would.</summary>
    public const int FortressPlunderMultiplier = 3;

    public static IReadOnlyList<Island> CreateIslands() =>
        Layout.Select((island, i) =>
        {
            var fortress = island.Kind == Kind.Fortress;
            var level = fortress ? island.Level ?? LevelAt(island.Center) : LevelAt(island.Center);
            var gold = Progression.PirateLevels.PlunderGold(level) * (fortress ? FortressPlunderMultiplier : 1);
            return Island.FromTemplate(i + 1, island.Template, island.Center, island.Area, island.Rotation, island.Kind == Kind.Shipyard,
                island.Name, gold, level, fortress);
        }).ToList();
}
