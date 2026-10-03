using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Abilities;

/// <summary>Every ability instance by <see cref="Ability.Id"/>, so the network can name abilities.</summary>
public static class AbilityRegistry
{
    private static readonly Dictionary<string, Ability> ById = Loadouts.Sloop
        .OfType<Ability>()
        .ToDictionary(a => a.Id);

    public static Ability? Find(string id) => ById.GetValueOrDefault(id);
}

/// <summary>Ability sets indexed by <see cref="AbilitySlot"/>; null means the slot is empty.</summary>
public static class Loadouts
{
    public static readonly IReadOnlyList<Ability?> Sloop = new Ability?[]
    {
        new BroadsideVolley(BroadsideSide.Port),      // 1
        new BroadsideVolley(BroadsideSide.Starboard), // 2
        null,                                         // 3
        null,                                         // 4
    };
}
