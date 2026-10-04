using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Abilities;

/// <summary>Every ability instance by <see cref="Ability.Id"/>, so the network can name abilities.</summary>
public static class AbilityRegistry
{
    private static readonly Dictionary<string, Ability> ById = WeaponCatalog.All.Select(w => w.Ability)
        .Concat(Loadouts.Pirate.OfType<Ability>())
        .DistinctBy(a => a.Id)
        .ToDictionary(a => a.Id);

    public static Ability? Find(string id) => ById.GetValueOrDefault(id);
}

/// <summary>Ability sets indexed by <see cref="AbilitySlot"/>; null means the slot is empty.</summary>
public static class Loadouts
{
    /// <summary>A player's sloop at the start of a run: the chosen weapon on 1, the rest of the slots free for purchases.</summary>
    public static IReadOnlyList<Ability?> Starting(Ability weapon) => new Ability?[] { weapon, null, null, null };

    /// <summary>Every player weapon at once, in catalog order (broadside on 1, long gun on 2, mortar on 3). For tests and sandboxes.</summary>
    public static readonly IReadOnlyList<Ability?> FullArsenal = WeaponCatalog.All
        .Select(w => (Ability?)w.Ability)
        .Concat(Enumerable.Repeat<Ability?>(null, Simulation.Ship.AbilitySlotCount))
        .Take(Simulation.Ship.AbilitySlotCount)
        .ToArray();

    /// <summary>A buccaneer's loadout, and the flagship's: the broadside only. Other pirates carry one other weapon (see <see cref="Progression.PirateRoles"/>).</summary>
    public static readonly IReadOnlyList<Ability?> Pirate = new Ability?[]
    {
        WeaponCatalog.Broadside.Ability,
        null,
        null,
        null,
    };
}
