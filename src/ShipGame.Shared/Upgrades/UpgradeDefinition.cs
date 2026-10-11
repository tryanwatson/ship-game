using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Upgrades;

/// <summary>
/// Something a shipyard sells. Each level bought adds one <see cref="StatModifier"/> tagged with
/// <see cref="Source"/>, so a ship's level in an upgrade is just how many it carries.
/// </summary>
/// <param name="Effect">Short description of one level, for the shipyard UI (e.g. "+6% MAX SPEED").</param>
/// <param name="CostGrowth">Each level costs this many times the one before, so the last levels are a real spend.</param>
public sealed record UpgradeDefinition(
    string Id,
    string Name,
    string Effect,
    StatId Stat,
    ModifierKind Kind,
    float ValuePerLevel,
    int BaseCost,
    float CostGrowth,
    int MaxLevel)
{
    public string Source => "upgrade:" + Id;

    /// <summary>Gold for the next level, given the level already owned.</summary>
    public int CostAt(int ownedLevel) => (int)MathF.Round(BaseCost * MathF.Pow(CostGrowth, Math.Max(0, ownedLevel)));

    public StatModifier Modifier => new(Stat, Kind, ValuePerLevel, Source);
}
