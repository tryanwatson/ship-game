using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Progression;

/// <summary>
/// Puts the map's pirates to sea at the start of a run. Each <see cref="PirateCamp"/> breaks up into groups of one to
/// <see cref="MaxGroupSize"/> that sail together, each pirate in them of any <see cref="PirateRole"/>, and then either guards the island it lies off (cruising round it and
/// going for anyone who comes near) or roams its sea. Which, and how the camp splits, is down to the run's seed. The
/// flagship patrols its waters at the far north. Bigger crews meet bigger camps. Camps don't come back once sunk.
/// </summary>
public static class PirateCamps
{
    /// <summary>Each player beyond the first adds this fraction to every camp.</summary>
    public const float SizePerExtraPlayer = 0.5f;

    /// <summary>No camp grows past this, whatever the crew.</summary>
    public const int MaxCampSize = 6;

    /// <summary>Pirates in a group, at most.</summary>
    public const int MaxGroupSize = 3;

    /// <summary>A camp this close to an island's shore (or nearer) may take up guarding it.</summary>
    public const float IslandReach = 12f;

    /// <summary>How likely a camp near an island is to guard it, rather than roam.</summary>
    public const double IslandGuardChance = 0.6;

    // An island's guards cruise between these distances off its shore, and go for anyone this close to it.
    public const float GuardInnerOffing = 3f;
    public const float GuardOuterOffing = 10f;
    public const float GuardWatchOffing = 14f;

    /// <summary>Rovers keep at least this far north of the start line.</summary>
    public const float StartBerth = 40f;

    /// <summary>The flagship cruises within this distance of its station, and goes for anyone within its watch.</summary>
    public const float FlagshipPatrolRadius = 8f;
    public const float FlagshipWatch = 24f;

    // Guards ride in a ring about the camp's center, clear of the shore.
    private const float RingRadius = 3.5f;
    private const float RingGrowth = 1.5f;
    private const float MinDistanceFromLand = 2.5f;

    /// <summary>Pirates in a camp of <paramref name="count"/> for a crew of <paramref name="players"/>.</summary>
    public static int CampSize(int count, int players = 1)
    {
        var scale = 1f + SizePerExtraPlayer * Math.Max(0, players - 1);
        return Math.Min((int)MathF.Ceiling(count * scale), Math.Max(count, MaxCampSize));
    }

    /// <summary>Spawns every camp and the flagship, sized for the players already in <paramref name="world"/>.</summary>
    public static void Populate(World world, IReadOnlyList<PirateCamp>? camps = null, int seed = 0)
    {
        var rng = new Random(seed);
        var players = Math.Max(1, world.Players.Count);
        foreach (var camp in camps ?? Archipelago.Camps)
        {
            var size = CampSize(camp.Count, players);
            var island = NearbyIsland(world, camp.Position);
            PirateOrders orders = island is not null && rng.NextDouble() < IslandGuardChance ? GuardOrders(island) : RoamOrdersFor(camp.Position);
            var placed = 0;
            while (placed < size)
            {
                var group = new PirateGroup();
                var groupSize = rng.Next(1, Math.Min(MaxGroupSize, size - placed) + 1);
                for (var i = 0; i < groupSize; i++, placed++)
                {
                    var pirate = SpawnPirate(world, GuardPosition(world, camp.Position, placed, size), ShipStats.PirateSloop, camp.Level,
                        PirateRoles.Loadout(PirateRoles.Pick(rng)));
                    group.Add(pirate);
                    pirate.Behavior = new HunterBehavior(orders, rng.Next(), groupSize > 1 ? group : null);
                }
            }
        }

        var flagship = SpawnPirate(world, Archipelago.BossPosition, ShipStats.Flagship, Archipelago.BossLevel, Loadouts.Pirate);
        flagship.IsBoss = true;
        flagship.Behavior = new HunterBehavior(new GuardPost(Archipelago.BossPosition, FlagshipPatrolRadius, Watch: FlagshipWatch), rng.Next());
    }

    /// <summary>The nearest island without a shipyard whose shore is within <see cref="IslandReach"/>, if any.</summary>
    private static Island? NearbyIsland(World world, Vector2 position) =>
        world.Islands
            .Where(i => !i.HasShipyard && Vector2.Distance(i.Center, position) - i.BoundingRadius <= IslandReach)
            .MinBy(i => Vector2.Distance(i.Center, position));

    public static GuardPost GuardOrders(Island island) =>
        new(island.Center, island.BoundingRadius + GuardOuterOffing, island.BoundingRadius + GuardInnerOffing,
            island.BoundingRadius + GuardWatchOffing, island.Id);

    /// <summary>The sea <paramref name="position"/> is in, kept clear of the start line.</summary>
    public static RoamOrders RoamOrdersFor(Vector2 position)
    {
        var sea = Archipelago.SeaAt(position);
        return new RoamOrders(sea.North, MathF.Min(sea.South, Archipelago.Start.Y - StartBerth));
    }

    private static Ship SpawnPirate(World world, Vector2 position, ShipStats hull, int level, IReadOnlyList<Ability?> loadout)
    {
        // Facing south, the way trouble comes from.
        var pirate = world.SpawnShip(position, MathF.PI / 2f, hull, abilities: loadout);
        pirate.Stance = NpcStance.Patrolling;
        PirateLevels.Apply(pirate, level);
        return pirate;
    }

    /// <summary>The <paramref name="index"/>th of <paramref name="size"/> guards: evenly round a ring, pushed out of any shallows.</summary>
    private static Vector2 GuardPosition(World world, Vector2 center, int index, int size)
    {
        if (size == 1 && world.DistanceToLand(center) >= MinDistanceFromLand)
            return center;
        var angle = MathF.Tau * index / size;
        var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var radius = RingRadius;
        var position = center + direction * radius;
        for (var attempt = 0; attempt < 8 && world.DistanceToLand(position) < MinDistanceFromLand; attempt++)
        {
            radius += RingGrowth;
            position = center + direction * radius;
        }
        return Vector2.Clamp(position, new Vector2(MinDistanceFromLand), world.WorldSize - new Vector2(MinDistanceFromLand));
    }
}
