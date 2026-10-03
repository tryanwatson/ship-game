using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Abilities;

/// <summary>
/// Numbers that skills can change about one ability (see <see cref="AbilityModifier"/>). Each ability reads the ones
/// it understands when it fires, so a skill's behavior lives in the ability's configuration rather than in checks
/// for the skill itself. Several start at 0 and switch a behavior on once a skill adds to them.
/// </summary>
public enum AbilityStat
{
    /// <summary>Multiplier on the ability's damage (stacks with the ship's <see cref="StatId.WeaponDamage"/>).</summary>
    Damage,

    /// <summary>Multiplier on the ability's cooldown: +0.3 reloads 30% slower, -0.3 30% faster.</summary>
    Cooldown,

    ProjectileSpeed,

    Range,

    /// <summary>Balls in a broadside, shells in a mortar salvo.</summary>
    ShotCount,

    BlastRadius,

    /// <summary>Multiplier on a shell's time in the air.</summary>
    FlightTime,

    /// <summary>Ships a shot passes through before it stops (0: it stops at the first).</summary>
    Pierce,

    /// <summary>Extra damage, as a fraction, for hits close to the muzzle (base 0).</summary>
    CloseRangeDamage,

    /// <summary>Extra damage, as a fraction, for shots fired while the ship is near full sail (base 0).</summary>
    SpeedDamage,

    /// <summary>Extra damage, as a fraction, for hits far from the muzzle (base 0).</summary>
    LongRangeDamage,

    /// <summary>Fraction of the ability's cooldown given back by a hit far from the muzzle (base 0).</summary>
    LongRangeRefund,

    /// <summary>Smaller blasts thrown out around each shell's impact (base 0).</summary>
    ClusterCount,
}

/// <summary>
/// A change to one ability's numbers. Combines like <see cref="StatModifier"/>: value = (base + flat) * (1 + percent).
/// </summary>
/// <param name="AbilityId">The <see cref="Ability.Id"/> it applies to.</param>
/// <param name="Source">What granted it (a skill's source tag), so it can be removed with its source.</param>
public readonly record struct AbilityModifier(string AbilityId, AbilityStat Stat, ModifierKind Kind, float Value, string Source);
