using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Upgrades;

/// <summary>
/// Something a shipyard sells. Each level bought adds one <see cref="StatModifier"/> tagged with
/// <see cref="Source"/>, so a ship's level in an upgrade is just how many it carries.
/// </summary>
/// <param name="Effect">Short description of one level, for the shipyard UI (e.g. "+6% MAX SPEED").</param>
public sealed record UpgradeDefinition(
    string Id,
    string Name,
    string Effect,
    StatId Stat,
    ModifierKind Kind,
    float ValuePerLevel,
    int BaseCost,
    int CostPerLevel,
    int MaxLevel)
{
    public string Source => "upgrade:" + Id;

    /// <summary>Gold for the next level, given the level already owned.</summary>
    public int CostAt(int ownedLevel) => BaseCost + CostPerLevel * ownedLevel;

    public StatModifier Modifier => new(Stat, Kind, ValuePerLevel, Source);
}
