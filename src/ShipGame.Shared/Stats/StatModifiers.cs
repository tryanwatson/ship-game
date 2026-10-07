using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Stats;

/// <summary>
/// A ship's upgrades. Effective stat = (base + sum of flat) * (1 + sum of percent) * product of multipliers, so flat
/// bonuses benefit from percentage ones, and the result never depends on the order upgrades were bought in.
/// </summary>
public sealed class StatModifiers
{
    private readonly List<StatModifier> _modifiers = new();

    public IReadOnlyList<StatModifier> All => _modifiers;

    public void Add(StatModifier modifier) => _modifiers.Add(modifier);

    public int RemoveSource(string source) => _modifiers.RemoveAll(m => m.Source == source);

    public float Apply(StatId stat, float baseValue) => Combine(_modifiers, stat, baseValue);

    /// <summary>
    /// However many percentage cuts stack up (three Glass Cannons, five Juggernauts), they leave this share of a stat:
    /// a ship with no health would sink the moment it spawned, again and again, and one with no speed couldn't move.
    /// </summary>
    public const float LeastPercentFactor = 0.1f;

    /// <summary>
    /// One stat through any set of modifiers: (base + flat) * (1 + percent) * every multiplier, never below 0, and the
    /// percentages never taking it below <see cref="LeastPercentFactor"/>.
    /// </summary>
    public static float Combine(IEnumerable<StatModifier> modifiers, StatId stat, float baseValue)
    {
        var flat = 0f;
        var percent = 0f;
        var multiplier = 1f;
        foreach (var modifier in modifiers)
        {
            if (modifier.Stat != stat)
                continue;
            switch (modifier.Kind)
            {
                case ModifierKind.Flat:
                    flat += modifier.Value;
                    break;
                case ModifierKind.Percent:
                    percent += modifier.Value;
                    break;
                default:
                    multiplier *= modifier.Value;
                    break;
            }
        }

        return MathF.Max(0f, (baseValue + flat) * MathF.Max(LeastPercentFactor, 1f + percent) * multiplier);
    }

    /// <summary>
    /// Upgrades can tighten a ship's turns to this share of its hull's radius and no further (2.5x the turn rate). Much
    /// past it the helm spins the ship faster than anyone can steer, and at zero she flips end for end every tick.
    /// </summary>
    public const float TightestTurn = 0.4f;

    public ShipStats Apply(ShipStats baseStats) => Apply(baseStats, _modifiers);

    /// <summary>The ship's stats through <paramref name="modifiers"/> (an upgrade list plus anything else, such as cards).</summary>
    public static ShipStats Apply(ShipStats baseStats, IReadOnlyCollection<StatModifier> modifiers)
    {
        float Stat(StatId stat, float baseValue) => Combine(modifiers, stat, baseValue);
        float TurnRadius(float baseValue) => MathF.Max(baseValue * TightestTurn, Stat(StatId.TurnRadius, baseValue));
        return baseStats with
        {
            MaxSpeed = Stat(StatId.MaxSpeed, baseStats.MaxSpeed),
            MaxHealth = Stat(StatId.MaxHealth, baseStats.MaxHealth),
            CooldownSpeed = Stat(StatId.CooldownSpeed, baseStats.CooldownSpeed),
            WeaponDamage = Stat(StatId.WeaponDamage, baseStats.WeaponDamage),
            ProjectileSpeed = Stat(StatId.ProjectileSpeed, baseStats.ProjectileSpeed),
            WeaponRange = Stat(StatId.WeaponRange, baseStats.WeaponRange),
            MinTurnRadius = TurnRadius(baseStats.MinTurnRadius),
            TurnRadiusAtMaxSpeed = TurnRadius(baseStats.TurnRadiusAtMaxSpeed),
            CargoCapacity = Stat(StatId.CargoCapacity, baseStats.CargoCapacity),
            HealthRegen = Stat(StatId.HealthRegen, baseStats.HealthRegen)
                + Stat(StatId.HealthRegenFraction, 0f) * Stat(StatId.MaxHealth, baseStats.MaxHealth),
        };
    }

    /// <summary>How many modifiers come from <paramref name="source"/> (e.g. levels bought of an upgrade).</summary>
    public int CountSource(string source) => _modifiers.Count(m => m.Source == source);
}
