using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Abilities;

/// <summary>Every ability instance by <see cref="Ability.Id"/>, so the network can name abilities.</summary>
public static class AbilityRegistry
{
    private static readonly Dictionary<string, Ability> ById = Loadouts.Sloop.Concat(Loadouts.Pirate)
        .OfType<Ability>()
        .DistinctBy(a => a.Id)
        .ToDictionary(a => a.Id);

    public static Ability? Find(string id) => ById.GetValueOrDefault(id);
}

/// <summary>Ability sets indexed by <see cref="AbilitySlot"/>; null means the slot is empty.</summary>
public static class Loadouts
{
    private static readonly BroadsideVolley Broadside = new();

    /// <summary>A player's sloop: the broadside on 1, the long gun on 2, the mortar on 3 (4 is free).</summary>
    public static readonly IReadOnlyList<Ability?> Sloop = new Ability?[]
    {
        Broadside,        // 1
        new LongGun(),    // 2
        new Mortar(),     // 3
        null,             // 4
    };

    /// <summary>Pirate sloops carry the broadside only (the hunter AI only knows how to use that).</summary>
    public static readonly IReadOnlyList<Ability?> Pirate = new Ability?[]
    {
        Broadside,
        null,
        null,
        null,
    };
}
