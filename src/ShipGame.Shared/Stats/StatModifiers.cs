using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Stats;

/// <summary>
/// A ship's upgrades. Effective stat = (base + sum of flat) * (1 + sum of percent), so flat bonuses benefit
/// from percentage ones, and the result never depends on the order upgrades were bought in.
/// </summary>
public sealed class StatModifiers
{
    private readonly List<StatModifier> _modifiers = new();

    public IReadOnlyList<StatModifier> All => _modifiers;

    public void Add(StatModifier modifier) => _modifiers.Add(modifier);

    public int RemoveSource(string source) => _modifiers.RemoveAll(m => m.Source == source);

    public float Apply(StatId stat, float baseValue)
    {
        var flat = 0f;
        var percent = 0f;
        foreach (var modifier in _modifiers)
        {
            if (modifier.Stat != stat)
                continue;
            if (modifier.Kind == ModifierKind.Flat)
                flat += modifier.Value;
            else
                percent += modifier.Value;
        }

        return MathF.Max(0f, (baseValue + flat) * (1f + percent));
    }

    public ShipStats Apply(ShipStats baseStats) => baseStats with
    {
        MaxSpeed = Apply(StatId.MaxSpeed, baseStats.MaxSpeed),
        MaxHealth = Apply(StatId.MaxHealth, baseStats.MaxHealth),
        CooldownSpeed = Apply(StatId.CooldownSpeed, baseStats.CooldownSpeed),
        WeaponDamage = Apply(StatId.WeaponDamage, baseStats.WeaponDamage),
        ProjectileSpeed = Apply(StatId.ProjectileSpeed, baseStats.ProjectileSpeed),
        WeaponRange = Apply(StatId.WeaponRange, baseStats.WeaponRange),
        MinTurnRadius = Apply(StatId.TurnRadius, baseStats.MinTurnRadius),
        TurnRadiusAtMaxSpeed = Apply(StatId.TurnRadius, baseStats.TurnRadiusAtMaxSpeed),
        CargoCapacity = Apply(StatId.CargoCapacity, baseStats.CargoCapacity),
        HealthRegen = Apply(StatId.HealthRegen, baseStats.HealthRegen)
            + Apply(StatId.HealthRegenFraction, 0f) * Apply(StatId.MaxHealth, baseStats.MaxHealth),
    };

    /// <summary>How many modifiers come from <paramref name="source"/> (e.g. levels bought of an upgrade).</summary>
    public int CountSource(string source) => _modifiers.Count(m => m.Source == source);
}
