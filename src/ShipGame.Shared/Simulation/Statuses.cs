using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Simulation;

/// <summary>Buffs and debuffs a ship can carry, player's or pirate's alike (see <see cref="World.ApplyStatus"/>).</summary>
public enum StatusId : byte
{
    /// <summary>Takes <see cref="StatusEffect.Power"/> damage a second for each stack, from whoever set it alight.</summary>
    Burning,

    /// <summary>Reloads <see cref="StatusEffect.Power"/> faster for each stack, and sails half that faster.</summary>
    Frenzy,

    /// <summary>Takes <see cref="StatusEffect.Power"/> more damage from everyone (Hunter's Mark).</summary>
    Marked,

    /// <summary>Sails <see cref="StatusEffect.Power"/> slower (Chain Shot).</summary>
    Slowed,

    /// <summary>Does <see cref="StatusEffect.Power"/> more damage with every weapon for each stack (Dug In: a stack a second at anchor).</summary>
    Entrenched,
}

/// <summary>How a status behaves: whether it's good for the ship, how many stacks it takes, how long each lasts.</summary>
/// <param name="Seconds">How long it lasts from the last time it was applied; then every stack comes off at once.</param>
public sealed record StatusDefinition(StatusId Id, string Name, bool IsBuff, int MaxStacks, float Seconds)
{
    public int Ticks => (int)MathF.Round(Seconds * SimConstants.TickRate);
}

/// <summary>One status on one ship. Reapplying it adds stacks, keeps the stronger power, and starts its clock again.</summary>
public sealed class StatusEffect
{
    public required StatusId Id { get; init; }

    public int Stacks { get; set; }

    /// <summary>What each stack does (damage a second, a fraction faster or slower...): see <see cref="StatusId"/>.</summary>
    public float Power { get; set; }

    /// <summary>When it wears off.</summary>
    public long UntilTick { get; set; }

    /// <summary>Who put it there, credited with what it does (a burn's damage, and the kill).</summary>
    public int SourceShipId { get; set; }

    public StatusDefinition Definition => Statuses.Get(Id);
}

public static class Statuses
{
    public static readonly IReadOnlyList<StatusDefinition> All = new StatusDefinition[]
    {
        new(StatusId.Burning, "BURNING", IsBuff: false, MaxStacks: 20, Seconds: 4f),
        new(StatusId.Frenzy, "FRENZY", IsBuff: true, MaxStacks: 10, Seconds: 5f),
        new(StatusId.Marked, "MARKED", IsBuff: false, MaxStacks: 1, Seconds: 5f),
        new(StatusId.Slowed, "SLOWED", IsBuff: false, MaxStacks: 1, Seconds: 3f),
        new(StatusId.Entrenched, "ENTRENCHED", IsBuff: true, MaxStacks: 10, Seconds: 3f),
    };

    private static readonly Dictionary<StatusId, StatusDefinition> ById = All.ToDictionary(s => s.Id);

    public static StatusDefinition Get(StatusId id) => ById[id];

    private const string SourcePrefix = "status:";

    /// <summary>Tags the stat modifiers a status puts on a ship, so they come off with it.</summary>
    public static string ModifierSource(StatusId id) => SourcePrefix + id;

    /// <summary>Whether a stat modifier is a status's (these don't outlast the ship they're on).</summary>
    public static bool IsStatusSource(string source) => source.StartsWith(SourcePrefix, StringComparison.Ordinal);

    /// <summary>The stat changes a status makes at <paramref name="stacks"/> stacks of <paramref name="power"/>.</summary>
    public static IEnumerable<StatModifier> ModifiersFor(StatusId id, int stacks, float power)
    {
        var source = ModifierSource(id);
        switch (id)
        {
            case StatusId.Frenzy:
                yield return new StatModifier(StatId.CooldownSpeed, ModifierKind.Percent, power * stacks, source);
                yield return new StatModifier(StatId.MaxSpeed, ModifierKind.Percent, power * stacks / 2f, source);
                break;
            case StatusId.Entrenched:
                yield return new StatModifier(StatId.WeaponDamage, ModifierKind.Percent, power * stacks, source);
                break;
            case StatusId.Slowed:
                yield return new StatModifier(StatId.MaxSpeed, ModifierKind.Multiplier, 1f - Math.Clamp(power, 0f, 0.95f), source);
                break;
        }
    }
}
