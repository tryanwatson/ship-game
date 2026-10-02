namespace ShipGame.Shared.Stats;

/// <summary>Ship stats that upgrades can modify. Add entries here (and in <see cref="StatModifiers"/>) as they become upgradable.</summary>
public enum StatId
{
    MaxSpeed,
    MaxHealth,

    /// <summary>Multiplier on how fast cooldowns tick down (base 1). Cooldown duration = base / CooldownSpeed.</summary>
    CooldownSpeed,

    /// <summary>Multiplier on weapon damage (base 1).</summary>
    WeaponDamage,

    /// <summary>Multiplier on projectile speed (base 1).</summary>
    ProjectileSpeed,

    /// <summary>Multiplier on weapon range (base 1).</summary>
    WeaponRange,

    /// <summary>Applies to both turning radii (tiles): negative percentages mean tighter turns.</summary>
    TurnRadius,
}

public enum ModifierKind
{
    /// <summary>Added to the base value before percentages apply, e.g. +20 max health.</summary>
    Flat,

    /// <summary>
    /// Fraction added to the percentage bonus, e.g. 0.05 for +5%. Percentages from all sources add together
    /// rather than compound, so ten +5% bonuses make +50%.
    /// </summary>
    Percent,
}

/// <param name="Source">
/// What granted the modifier ("kill-reward", a shop item id, ...), so everything from one source can be removed
/// together, e.g. when an item is sold.
/// </param>
public readonly record struct StatModifier(StatId Stat, ModifierKind Kind, float Value, string Source);
