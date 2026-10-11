using System.Numerics;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Maps;

/// <summary>
/// The patch of sea a stop on the chart is played in: its size, its islands, and where the crew sails in (near the
/// south edge, facing north).
/// </summary>
/// <param name="Objective">The stop's fortress or port island; null for open water and a boss's arena.</param>
public sealed record RegionLayout(Vector2 Size, IReadOnlyList<Island> Islands, Vector2 Entry, int? Objective = null);

/// <summary>
/// Builds each stop's patch of sea, from the run's seed: small (a few screens across), with the stop's fortress or
/// port to the north of where the crew comes in and a scattering of plain islands for cover and plunder. Every
/// island is a hand-drawn outline, turned and sized at random. A fortress's island is made big enough to stand all its
/// forts round its shore, which a bigger crew makes more of (see <see cref="Fortresses.Forts(int, int)"/>), and its sea
/// grows round it.
/// </summary>
public static class Regions
{
    public static readonly Vector2 StartSize = new(140f, 140f);
    public static readonly Vector2 FortressSize = new(180f, 180f);
    public static readonly Vector2 PortSize = new(140f, 140f);
    public static readonly Vector2 BossSize = new(200f, 200f);

    /// <summary>Facing north, up the screen.</summary>
    public const float EntryHeading = -MathF.PI / 2f;

    /// <summary>How far in from the south edge the crew comes in.</summary>
    public const float EntryOffing = 22f;

    /// <summary>The biggest crew a region makes room for at its entry (the server's limit).</summary>
    public const int MaxCrew = 12;

    /// <summary>Half the width of the line a full crew comes in on, abreast.</summary>
    public const float EntryHalfWidth = (MaxCrew - 1) * Progression.Runs.StartSpacing / 2f;

    /// <summary>No island comes nearer than this to any point of the entry line (from its shore, roughly).</summary>
    public const float EntryBerth = 20f;

    /// <summary>Islands keep at least this far inside the edges.</summary>
    public const float EdgeBerth = 12f;

    /// <summary>Open water between neighbouring islands, at least (between the circles round them).</summary>
    public const float Channel = 16f;

    /// <summary>A fortress's island: bigger the higher its level.</summary>
    public static float FortressArea(int level) => 300f + 20f * Math.Clamp(level, 1, 8);

    /// <summary>
    /// A fortress's island for a crew of <paramref name="players"/>: at least <see cref="FortressArea(int)"/>, and enough
    /// that a round island would have <see cref="Fortresses.ShorePerFort"/> of shore for every fort (real outlines have
    /// more shore than a circle, so the forts stand a little further apart than that).
    /// </summary>
    public static float FortressArea(int level, int players)
    {
        var shore = Fortresses.Forts(level, players) * Fortresses.ShorePerFort;
        return MathF.Max(FortressArea(level), shore * shore / (4f * MathF.PI));
    }

    /// <summary>A fortress's sea is at least <see cref="FortressSize"/>, and this much wider than its island.</summary>
    public const float FortressSeaRoom = 130f;

    /// <summary>The start's islands are rich (there's a pirate camp to see off first): this many times an ordinary island's gold.</summary>
    public const int StartPlunderMultiplier = 3;

    public const float PortArea = 200f;
    public const float MinPlainArea = 110f;
    public const float MaxPlainArea = 220f;

    /// <summary>Plundering a fallen fortress pays this many times what an ordinary island of its level would.</summary>
    public const int FortressPlunderMultiplier = Archipelago.FortressPlunderMultiplier;

    private const int PlacementAttempts = 200;

    /// <summary>
    /// The sea for <paramref name="node"/>, for a crew of <paramref name="players"/>. Island ids start at
    /// <paramref name="firstIslandId"/>, so they never repeat within a run.
    /// </summary>
    public static RegionLayout Build(ChartNode node, int seed, int firstIslandId, int players = 1)
    {
        var rng = new Random(SeaChart.MixSeed(seed, node.Id));
        var size = SizeFor(node.Kind);
        // A fortress's island is sized first, so the sea can be made to fit it, and placed once the sea is.
        var (fortressShape, fortressTurn, fortressArea, fortressRadius) = (Array.Empty<Vector2>(), 0f, 0f, 0f);
        if (node.Kind == NodeKind.Fortress)
        {
            (fortressShape, fortressTurn, fortressArea) = (Shape(rng), Turn(rng), FortressArea(node.Level, players));
            fortressRadius = Island.FromTemplate(firstIslandId, fortressShape, Vector2.Zero, fortressArea, fortressTurn).BoundingRadius;
            var across = MathF.Max(FortressSize.X, 2f * fortressRadius + FortressSeaRoom);
            size = new Vector2(across, across);
        }
        var entry = new Vector2(size.X / 2f, size.Y - EntryOffing);
        var islands = new List<Island>();
        var names = new HashSet<string>();
        var nextId = firstIslandId;
        int? objective = null;

        switch (node.Kind)
        {
            case NodeKind.Fortress:
            {
                var center = new Vector2(size.X / 2f + Jitter(rng, 20f), MathF.Max(size.Y * 0.32f, fortressRadius + EdgeBerth + 8f) + Jitter(rng, 8f));
                var gold = PirateLevels.PlunderGold(node.Level) * FortressPlunderMultiplier;
                islands.Add(Island.FromTemplate(nextId, fortressShape, center, fortressArea, fortressTurn,
                    name: Named(node, names, () => IslandNames.Fortress(rng, node.Difficulty, names)), plunderGold: gold, level: node.Level, isFortress: true));
                objective = nextId++;
                break;
            }
            case NodeKind.Port:
            {
                var center = new Vector2(size.X / 2f + Jitter(rng, 10f), size.Y * 0.42f);
                islands.Add(Island.FromTemplate(nextId, Shape(rng), center, PortArea, Turn(rng), hasShipyard: true,
                    name: Named(node, names, () => IslandNames.Port(rng, names)), plunderGold: PirateLevels.PlunderGold(node.Level), level: node.Level));
                objective = nextId++;
                break;
            }
        }

        var plain = node.Kind switch
        {
            NodeKind.Start => 2,
            NodeKind.Port => 1 + rng.Next(2),
            NodeKind.Boss => 3 + rng.Next(3),
            _ => 2 + rng.Next(3),
        };
        for (var i = 0; i < plain; i++)
        {
            var area = MinPlainArea + (float)rng.NextDouble() * (MaxPlainArea - MinPlainArea);
            var plunder = PirateLevels.PlunderGold(node.Level) * (node.Kind == NodeKind.Start ? StartPlunderMultiplier : 1);
            if (Place(rng, size, entry, islands, nextId, area, IslandNames.Plain(rng, names), node.Level, plunder) is { } island)
            {
                islands.Add(island);
                nextId++;
            }
        }
        return new RegionLayout(size, islands, entry, objective);
    }

    public static Vector2 SizeFor(NodeKind kind) => kind switch
    {
        NodeKind.Start => StartSize,
        NodeKind.Port => PortSize,
        NodeKind.Boss => BossSize,
        _ => FortressSize,
    };

    /// <summary>A plain island somewhere clear: inside the edges, off the entry, and apart from the rest. Null if there's no room.</summary>
    private static Island? Place(Random rng, Vector2 size, Vector2 entry, List<Island> placed, int id, float area, string name, int level, int plunderGold)
    {
        var shape = Shape(rng);
        var turn = Turn(rng);
        for (var attempt = 0; attempt < PlacementAttempts; attempt++)
        {
            var center = new Vector2(Between(rng, EdgeBerth, size.X - EdgeBerth), Between(rng, EdgeBerth, size.Y - EdgeBerth));
            var island = Island.FromTemplate(id, shape, center, area, turn, name: name, plunderGold: plunderGold, level: level);
            var r = island.BoundingRadius;
            if (center.X - r < EdgeBerth || center.Y - r < EdgeBerth || center.X + r > size.X - EdgeBerth || center.Y + r > size.Y - EdgeBerth)
                continue;
            if (DistanceToEntry(center, entry) < EntryBerth + r)
                continue;
            if (placed.Any(other => Vector2.Distance(center, other.Center) < r + other.BoundingRadius + Channel))
                continue;
            return island;
        }
        return null;
    }

    /// <summary>How far <paramref name="point"/> is from the line a full crew comes in on.</summary>
    public static float DistanceToEntry(Vector2 point, Vector2 entry)
    {
        var along = Math.Clamp(point.X, entry.X - EntryHalfWidth, entry.X + EntryHalfWidth);
        return Vector2.Distance(point, new Vector2(along, entry.Y));
    }

    private static Vector2[] Shape(Random rng) => IslandShapes.All[rng.Next(IslandShapes.All.Count)];

    private static float Turn(Random rng) => (float)rng.NextDouble() * MathF.Tau;

    private static float Jitter(Random rng, float range) => ((float)rng.NextDouble() * 2f - 1f) * range;

    private static float Between(Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

    /// <summary>The stop's own island: the name it has on the chart (a fresh one, if none), its words taken.</summary>
    private static string Named(ChartNode node, HashSet<string> taken, Func<string> deal)
    {
        if (node.Name.Length == 0)
            return deal();
        IslandNames.Take(node.Name, taken);
        return node.Name;
    }
}
