using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Abilities;

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
