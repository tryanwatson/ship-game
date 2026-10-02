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
    };
}
