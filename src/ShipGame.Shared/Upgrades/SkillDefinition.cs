using ShipGame.Shared.Abilities;
using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Upgrades;

/// <summary>One change a skill makes to its ability's numbers.</summary>
public readonly record struct SkillEffect(AbilityStat Stat, ModifierKind Kind, float Value)
{
    public static SkillEffect Flat(AbilityStat stat, float value) => new(stat, ModifierKind.Flat, value);

    public static SkillEffect Percent(AbilityStat stat, float value) => new(stat, ModifierKind.Percent, value);
}

/// <summary>
/// A node in one weapon's skill tree, bought once with gold at a shipyard. What it does is entirely its
/// <see cref="Effects"/>: the ability reads those as configuration, so nothing else needs to know the skill exists.
/// </summary>
/// <param name="AbilityId">The weapon whose tree it's in; the ship must have unlocked it.</param>
/// <param name="Description">What it does, in the shipyard's capitals.</param>
public sealed record SkillDefinition(string Id, string Name, string AbilityId, int Cost, string Description)
{
    /// <summary>Skills that must all be owned first.</summary>
    public IReadOnlyList<string> Requires { get; init; } = Array.Empty<string>();

    /// <summary>Skills of which at least one must be owned first (none: no such requirement). For capstones that crown either branch.</summary>
    public IReadOnlyList<string> RequiresAny { get; init; } = Array.Empty<string>();

    /// <summary>Skills this one can't be owned alongside, either way round: buying one closes off the other and its branch.</summary>
    public IReadOnlyList<string> Excludes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<SkillEffect> Effects { get; init; } = Array.Empty<SkillEffect>();

    /// <summary>Tags the modifiers it grants, like an upgrade's source.</summary>
    public string Source => "skill:" + Id;

    public IEnumerable<AbilityModifier> Modifiers =>
        Effects.Select(e => new AbilityModifier(AbilityId, e.Stat, e.Kind, e.Value, Source));

    /// <summary>Every skill this one builds on directly.</summary>
    public IEnumerable<string> Prerequisites => Requires.Concat(RequiresAny);
}

/// <summary>Where a skill stands for a particular ship.</summary>
public enum SkillStatus
{
    Owned,

    /// <summary>Can be bought now (gold permitting).</summary>
    Available,

    /// <summary>The weapon itself is still locked.</summary>
    WeaponLocked,

    /// <summary>Builds on a skill not yet owned.</summary>
    NeedsPrerequisite,

    /// <summary>Closed off by a choice already made.</summary>
    Excluded,
}
