using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Upgrades;

/// <summary>
/// Everything a shipyard sells. Add rows here; the shipyard UI lists them in this order. How many levels of each a
/// particular shipyard stocks depends on how far out it is (see <see cref="Shipyards.StockedLevels"/>).
/// </summary>
public static class UpgradeCatalog
{
    /// <summary>Each level of an upgrade costs half as much again as the last: 10, 15, 22, 34... 171 for the eighth.</summary>
    public const float Growth = 1.5f;

    public static readonly IReadOnlyList<UpgradeDefinition> All = new UpgradeDefinition[]
    {
        new("hull", "HULL", "+20 MAX HP", StatId.MaxHealth, ModifierKind.Flat, 20f, BaseCost: 10, CostGrowth: Growth, MaxLevel: 8),
        new("repairs", "REPAIRS", "+0.5% MAX HP PER SECOND", StatId.HealthRegenFraction, ModifierKind.Flat, 0.005f, BaseCost: 10, CostGrowth: Growth, MaxLevel: 8),
        new("speed", "SPEED", "+6% MAX SPEED", StatId.MaxSpeed, ModifierKind.Percent, 0.06f, BaseCost: 10, CostGrowth: Growth, MaxLevel: 8),
        new("reload", "RELOAD", "+8% COOLDOWN SPEED", StatId.CooldownSpeed, ModifierKind.Percent, 0.08f, BaseCost: 10, CostGrowth: Growth, MaxLevel: 8),
        new("damage", "DAMAGE", "+10% DAMAGE", StatId.WeaponDamage, ModifierKind.Percent, 0.10f, BaseCost: 15, CostGrowth: Growth, MaxLevel: 8),
        new("shot-speed", "SHOT SPEED", "+10% SHOT SPEED", StatId.ProjectileSpeed, ModifierKind.Percent, 0.10f, BaseCost: 10, CostGrowth: Growth, MaxLevel: 8),
        new("range", "RANGE", "+10% RANGE", StatId.WeaponRange, ModifierKind.Percent, 0.10f, BaseCost: 15, CostGrowth: Growth, MaxLevel: 8),
        new("agility", "AGILITY", "-8% TURN RADIUS", StatId.TurnRadius, ModifierKind.Percent, -0.08f, BaseCost: 10, CostGrowth: Growth, MaxLevel: 8),
    };

    public static UpgradeDefinition? Find(string id) => All.FirstOrDefault(u => u.Id == id);
}
