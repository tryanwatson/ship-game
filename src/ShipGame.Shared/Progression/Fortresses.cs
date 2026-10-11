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
/// It's taken once every fort has fallen, whatever its ships are doing (see <see cref="RunDirector"/>). A bigger crew
/// meets more forts and more guards, not just sturdier ones: the guns grow with the crew (<see cref="CountScale"/>),
/// so each sailor still has as many trained on them, and the island grows to make room for them on its shore.
/// </summary>
public static class Fortresses
{
    /// <summary>Each player beyond the first adds this fraction to everything a fortress or boss has to be worn through.</summary>
    public const float TotalPerExtraPlayer = 0.75f;

    /// <summary>Each player beyond the first adds this fraction to the number of forts.</summary>
    public const float CountPerExtraPlayer = 0.5f;

    /// <summary>
    /// Each player beyond the first adds this fraction to the damage of a fort's or a boss's guns: with the crew spread
    /// round a bigger island (or all round a flagship), not every gun can bear on every sailor, so each hits a little
    /// harder. Ships that sail after the crew don't need it.
    /// </summary>
    public const float DamagePerExtraPlayer = 0.06f;

    /// <summary>The source tag on the damage a crew's size adds to pirate guns (kept apart from <see cref="CrewSizeSource"/>, which gold reads).</summary>
    public const string CrewGunsSource = "crew-guns";

    /// <summary>Gives a fort or boss the extra damage a crew of <paramref name="players"/> brings out (see <see cref="DamagePerExtraPlayer"/>).</summary>
    public static void ArmForCrew(Ship pirate, int players)
    {
        pirate.RemoveModifiers(CrewGunsSource);
        if (players > 1)
            pirate.AddModifier(new StatModifier(StatId.WeaponDamage, ModifierKind.Percent, DamagePerExtraPlayer * (players - 1), CrewGunsSource));
    }

    public const string CrewSizeSource = "crew-size";

    /// <summary>
    /// How much more there is to wear through (and to win) for a crew of <paramref name="players"/>: every fort's
    /// health together, a boss's, plunder. +75% a sailor past the first.
    /// </summary>
    public static float CrewScale(int players) => 1f + TotalPerExtraPlayer * Math.Max(0, players - 1);

    /// <summary>How many more forts a crew of <paramref name="players"/> meets: +50% a sailor past the first.</summary>
    public static float CountScale(int players) => 1f + CountPerExtraPlayer * Math.Max(0, players - 1);

    /// <summary>What each fort's health is multiplied by, so that the forts together come to <see cref="CrewScale"/>.</summary>
    public static float FortHealthScale(int players) => CrewScale(players) / CountScale(players);

    /// <summary>How far inland of the shoreline a fort's center stands; its walls reach out over the water.</summary>
    public const float ShoreInset = 0.3f;

    /// <summary>Guards start this far off the shore, on the side of the island facing the crew's approach.</summary>
    public const float GuardOffing = 7f;

    /// <summary>At most this many guards muster together; a bigger garrison splits round the island's near side.</summary>
    public const int GuardsPerPack = 4;

    /// <summary>Shore an island needs for each fort round it, roughly (so a fort's guns overlap its neighbours').</summary>
    public const float ShorePerFort = 6.5f;

    /// <summary>Batteries round a fortress of <paramref name="level"/> for a crew of one: 2 at level 1, one more every other level.</summary>
    public static int Batteries(int level) => 2 + (Math.Max(1, level) - 1) / 2;

    /// <summary>Mortar towers for a crew of one: none at level 1, one more every other level.</summary>
    public static int MortarTowers(int level) => Math.Max(1, level) / 2;

    /// <summary>Guard ships for a crew of one; see <see cref="PirateCamps.CampSize"/> for bigger crews.</summary>
    public static int GuardShips(int level) => 1 + Math.Max(1, level) / 2;

    public static int Forts(int level) => Batteries(level) + MortarTowers(level);

    /// <summary>Forts round a fortress of <paramref name="level"/> for a crew of <paramref name="players"/>.</summary>
    public static int Forts(int level, int players) => (int)MathF.Round(Forts(level) * CountScale(players));

    /// <summary>How many of <see cref="Forts(int, int)"/> are mortar towers: the same share as a crew of one meets.</summary>
    public static int MortarTowers(int level, int players) =>
        (int)MathF.Round(Forts(level, players) * MortarTowers(level) / (float)Forts(level));

    public static int Batteries(int level, int players) => Forts(level, players) - MortarTowers(level, players);

    /// <summary>
    /// Puts a fortress's forts on its shore and its guards to sea, for a crew of <paramref name="players"/>. The guards
    /// gather on the side facing <paramref name="approach"/>, where trouble comes from (the middle of the old map if
    /// not given), in packs spread round that side. <paramref name="extraGuards"/> more join them (a rougher stop that
    /// didn't earn a level of its own).
    /// </summary>
    public static void Garrison(World world, Island island, int players, Random rng, Vector2? approach = null, int extraGuards = 0)
    {
        var level = island.Level;
        var forts = Forts(level, players);
        var mortars = MortarTowers(level, players);
        var turn = (float)rng.NextDouble() * MathF.Tau;
        for (var i = 0; i < forts; i++)
        {
            // Mortar towers spread evenly among the batteries.
            var kind = mortars > 0 && i * mortars % forts < mortars ? FortKind.MortarTower : FortKind.Battery;
            var angle = turn + MathF.Tau * i / forts;
            SpawnFort(world, island, angle, kind, players);
        }

        var from = approach ?? Maps.Archipelago.Start;
        var toStart = from - island.Center;
        var facing = toStart.LengthSquared() > 1e-6f ? Vector2.Normalize(toStart) : Vector2.UnitX;
        var guards = PirateCamps.CampSize(GuardShips(level) + Math.Max(0, extraGuards), players);
        var packs = (guards + GuardsPerPack - 1) / GuardsPerPack;
        for (var p = 0; p < packs; p++)
        {
            // Fanned across the near side: one pack straight toward the approach, more either side of it.
            var swing = packs == 1 ? 0f : (p / (float)(packs - 1) - 0.5f) * MathF.PI * 0.9f;
            var side = Geometry.Rotate(facing, swing);
            var muster = island.ShoreToward(side) + side * GuardOffing;
            var size = guards / packs + (p < guards % packs ? 1 : 0);
            PirateCamps.SpawnPack(world, muster, size, level, PirateCamps.GuardOrders(island), rng, from);
        }
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
            fort.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Multiplier, FortHealthScale(players), CrewSizeSource));
        ArmForCrew(fort, players);
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
