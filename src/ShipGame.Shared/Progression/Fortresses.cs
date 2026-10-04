using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Progression;

/// <summary>The two kinds of gun a fortress mounts on its shore.</summary>
public enum FortKind
{
    /// <summary>A long gun: picks off ships that come within reach, if its own island isn't all that's between them.</summary>
    Battery,

    /// <summary>A mortar: lobs shells at ships further out, over anything.</summary>
    MortarTower,
}

/// <summary>
/// Fortress islands (<see cref="Island.IsFortress"/>) and their garrisons. A fortress's level sets everything about
/// its defense: how many batteries and mortar towers stand round its shore (forts: immovable pirate "ships" pinned to
/// the island, with <see cref="Ship.FortIslandId"/> set), and how many ships of its level cruise round it, guarding it.
/// It's taken once every fort has fallen, whatever its ships are doing (see <see cref="RunDirector"/>). Bigger crews
/// meet sturdier forts and more guards.
/// </summary>
public static class Fortresses
{
    /// <summary>Each player beyond the first adds this fraction to a fort's health.</summary>
    public const float HealthPerExtraPlayer = 0.5f;

    public const string CrewSizeSource = "crew-size";

    /// <summary>How far inland of the shoreline a fort's center stands; its walls reach out over the water.</summary>
    public const float ShoreInset = 0.3f;

    /// <summary>Guards start this far off the shore, on the side of the island facing the start.</summary>
    public const float GuardOffing = 7f;

    /// <summary>Batteries round a fortress of <paramref name="level"/>: 2 at level 1, one more every other level.</summary>
    public static int Batteries(int level) => 2 + (Math.Max(1, level) - 1) / 2;

    /// <summary>Mortar towers: none at level 1, one more every other level.</summary>
    public static int MortarTowers(int level) => Math.Max(1, level) / 2;

    /// <summary>Guard ships for a crew of one; see <see cref="PirateCamps.CampSize"/> for bigger crews.</summary>
    public static int GuardShips(int level) => 1 + Math.Max(1, level) / 2;

    public static int Forts(int level) => Batteries(level) + MortarTowers(level);

    /// <summary>Puts a fortress's forts on its shore and its guards to sea, for a crew of <paramref name="players"/>.</summary>
    public static void Garrison(World world, Island island, int players, Random rng)
    {
        var level = island.Level;
        var forts = Forts(level);
        var mortars = MortarTowers(level);
        var turn = (float)rng.NextDouble() * MathF.Tau;
        for (var i = 0; i < forts; i++)
        {
            // Mortar towers spread evenly among the batteries.
            var kind = mortars > 0 && i * mortars % forts < mortars ? FortKind.MortarTower : FortKind.Battery;
            var angle = turn + MathF.Tau * i / forts;
            SpawnFort(world, island, angle, kind, players);
        }

        // Guards gather on the side facing the middle of the map, where trouble comes from.
        var toStart = Maps.Archipelago.Start - island.Center;
        var facing = toStart.LengthSquared() > 1e-6f ? Vector2.Normalize(toStart) : Vector2.UnitX;
        var muster = island.ShoreToward(facing) + facing * GuardOffing;
        PirateCamps.SpawnPack(world, muster, PirateCamps.CampSize(GuardShips(level), players), level,
            PirateCamps.GuardOrders(island), rng);
    }

    /// <summary>A fort of <paramref name="kind"/> on <paramref name="island"/>'s shore, straight out from its center along <paramref name="angle"/>.</summary>
    public static Ship SpawnFort(World world, Island island, float angle, FortKind kind, int players = 1)
    {
        var outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var position = island.ShoreToward(outward) - outward * ShoreInset;
        var weapon = kind == FortKind.MortarTower ? WeaponCatalog.Mortar.Ability : WeaponCatalog.LongGun.Ability;
        var fort = world.SpawnShip(position, angle, ShipStats.Fort, abilities: Loadouts.Starting(weapon));
        fort.FortIslandId = island.Id;
        fort.IsAnchored = true;
        fort.Stance = NpcStance.Patrolling;
        PirateLevels.Apply(fort, island.Level);
        if (players > 1)
            fort.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Multiplier, 1f + HealthPerExtraPlayer * (players - 1), CrewSizeSource));
        fort.Health = fort.Stats.MaxHealth;
        fort.Behavior = new FortBehavior();
        return fort;
    }

    /// <summary>What kind of fort <paramref name="fort"/> is, by its gun.</summary>
    public static FortKind KindOf(Ship fort) =>
        fort.Abilities[(int)AbilitySlot.One]?.Definition is Mortar ? FortKind.MortarTower : FortKind.Battery;

    public static string Name(FortKind kind) => kind == FortKind.MortarTower ? "MORTAR TOWER" : "BATTERY";

    /// <summary>Forts still standing on <paramref name="island"/>.</summary>
    public static int Standing(World world, Island island) => world.Ships.Count(s => s.FortIslandId == island.Id && !s.IsSunk);
}
