using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Progression;

/// <summary>
/// Puts the map's pirates to sea at the start of a run: every fortress's garrison (see <see cref="Fortresses"/>), and
/// the roaming packs. A pack breaks up into groups of one to <see cref="MaxGroupSize"/> that sail together, each
/// pirate in them of any <see cref="PirateRole"/>, roaming the ring of sea the pack starts in. Fortress guards are
/// packs too, cruising round their island instead. How each splits is down to the run's seed. Bigger crews meet
/// bigger packs. Pirates don't come back once sunk.
/// </summary>
public static class PirateCamps
{
    /// <summary>Each player beyond the first adds this fraction to every pack.</summary>
    public const float SizePerExtraPlayer = 0.5f;

    /// <summary>No pack grows past this, whatever the crew (a fortress splits its guards into several; see <see cref="Fortresses.Garrison"/>).</summary>
    public const int MaxCampSize = 16;

    /// <summary>Pirates in a group, at most.</summary>
    public const int MaxGroupSize = 3;

    // An island's guards cruise between these distances off its shore, and go for anyone this close to it.
    public const float GuardInnerOffing = 3f;
    public const float GuardOuterOffing = 10f;
    public const float GuardWatchOffing = 14f;

    /// <summary>Rovers keep at least this far from the start.</summary>
    public const float StartBerth = 60f;

    // A pack forms up in a ring about its center, clear of the shore.
    private const float RingRadius = 3.5f;
    private const float RingGrowth = 1.5f;
    private const float MinDistanceFromLand = 2.5f;

    /// <summary>Pirates in a pack of <paramref name="count"/> for a crew of <paramref name="players"/>.</summary>
    public static int CampSize(int count, int players = 1)
    {
        var scale = 1f + SizePerExtraPlayer * Math.Max(0, players - 1);
        return Math.Min((int)MathF.Ceiling(count * scale), Math.Max(count, MaxCampSize));
    }

    /// <summary>Garrisons every fortress and sends out the rovers, sized for the players already in <paramref name="world"/>.</summary>
    public static void Populate(World world, IReadOnlyList<PirateCamp>? camps = null, int seed = 0)
    {
        var rng = new Random(seed);
        var players = Math.Max(1, world.Players.Count);
        foreach (var island in world.Islands.Where(i => i.IsFortress))
            Fortresses.Garrison(world, island, players, rng);
        foreach (var camp in camps ?? Archipelago.Camps)
            SpawnPack(world, camp.Position, CampSize(camp.Count, players), camp.Level, RoamOrdersFor(camp.Position), rng);
    }

    /// <summary>
    /// <paramref name="size"/> pirates of <paramref name="level"/> round <paramref name="center"/>, split into groups
    /// that each sail under <paramref name="orders"/>, facing <paramref name="facing"/> (the middle of the old map if not given).
    /// </summary>
    public static void SpawnPack(World world, Vector2 center, int size, int level, PirateOrders orders, Random rng, Vector2? facing = null)
    {
        var placed = 0;
        while (placed < size)
        {
            var group = new PirateGroup();
            var groupSize = rng.Next(1, Math.Min(MaxGroupSize, size - placed) + 1);
            for (var i = 0; i < groupSize; i++, placed++)
            {
                var pirate = SpawnPirate(world, PackPosition(world, center, placed, size), ShipStats.PirateSloop, level,
                    PirateRoles.Loadout(PirateRoles.Pick(rng)), facing ?? Archipelago.Start);
                group.Add(pirate);
                pirate.Behavior = new HunterBehavior(orders, rng.Next(), groupSize > 1 ? group : null);
            }
        }
    }

    public static GuardPost GuardOrders(Island island) =>
        new(island.Center, island.BoundingRadius + GuardOuterOffing, island.BoundingRadius + GuardInnerOffing,
            island.BoundingRadius + GuardWatchOffing, island.Id);

    /// <summary>The ring of sea <paramref name="position"/> is in, kept clear of the start.</summary>
    public static RoamOrders RoamOrdersFor(Vector2 position)
    {
        var sea = Archipelago.SeaAt(position);
        var furthest = Archipelago.Size.Length() / 2f; // the corners
        return new RoamOrders(Archipelago.Start, MathF.Max(sea.InnerRadius, StartBerth), MathF.Min(sea.OuterRadius, furthest));
    }

    private static Ship SpawnPirate(World world, Vector2 position, ShipStats hull, int level, IReadOnlyList<Ability?> loadout, Vector2 facing)
    {
        // Facing the way trouble comes from.
        var toStart = facing - position;
        var pirate = world.SpawnShip(position, MathF.Atan2(toStart.Y, toStart.X), hull, abilities: loadout);
        pirate.Stance = NpcStance.Patrolling;
        PirateLevels.Apply(pirate, level);
        return pirate;
    }

    /// <summary>
    /// The <paramref name="index"/>th of <paramref name="size"/> pirates: evenly round a ring. A spot in the shallows
    /// gives way to one further round and further out, until there's open water.
    /// </summary>
    private static Vector2 PackPosition(World world, Vector2 center, int index, int size)
    {
        var min = new Vector2(MinDistanceFromLand);
        var max = world.WorldSize - min;
        if (size == 1 && world.DistanceToLand(center) >= MinDistanceFromLand)
            return Vector2.Clamp(center, min, max);
        var best = center;
        var bestClearance = float.MinValue;
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var angle = MathF.Tau * index / size + attempt * GoldenAngle;
            var radius = RingRadius + RingGrowth * (attempt / 2);
            var position = Vector2.Clamp(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius, min, max);
            var clearance = world.DistanceToLand(position);
            if (clearance >= MinDistanceFromLand)
                return position;
            if (clearance > bestClearance)
            {
                best = position;
                bestClearance = clearance;
            }
        }
        return best;
    }

    // Steps round a circle without ever lining up with earlier steps.
    private const float GoldenAngle = 2.39996f;
}
