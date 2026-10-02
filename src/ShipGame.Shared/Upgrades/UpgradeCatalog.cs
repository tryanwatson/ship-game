using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Upgrades;

/// <summary>Everything a shipyard sells. Add rows here; the shipyard UI lists them in this order.</summary>
public static class UpgradeCatalog
{
    public static readonly IReadOnlyList<UpgradeDefinition> All = new UpgradeDefinition[]
    {
        new("hull", "HULL", "+20 MAX HP", StatId.MaxHealth, ModifierKind.Flat, 20f, BaseCost: 10, CostPerLevel: 5, MaxLevel: 5),
        new("speed", "SPEED", "+6% MAX SPEED", StatId.MaxSpeed, ModifierKind.Percent, 0.06f, BaseCost: 10, CostPerLevel: 5, MaxLevel: 5),
        new("reload", "RELOAD", "+8% COOLDOWN SPEED", StatId.CooldownSpeed, ModifierKind.Percent, 0.08f, BaseCost: 10, CostPerLevel: 5, MaxLevel: 5),
        new("damage", "DAMAGE", "+10% DAMAGE", StatId.WeaponDamage, ModifierKind.Percent, 0.10f, BaseCost: 15, CostPerLevel: 5, MaxLevel: 5),
        new("shot-speed", "SHOT SPEED", "+10% SHOT SPEED", StatId.ProjectileSpeed, ModifierKind.Percent, 0.10f, BaseCost: 10, CostPerLevel: 5, MaxLevel: 5),
        new("range", "RANGE", "+10% RANGE", StatId.WeaponRange, ModifierKind.Percent, 0.10f, BaseCost: 15, CostPerLevel: 5, MaxLevel: 5),
        new("agility", "AGILITY", "-8% TURN RADIUS", StatId.TurnRadius, ModifierKind.Percent, -0.08f, BaseCost: 10, CostPerLevel: 5, MaxLevel: 5),
    };

    public static UpgradeDefinition? Find(string id) => All.FirstOrDefault(u => u.Id == id);
}
