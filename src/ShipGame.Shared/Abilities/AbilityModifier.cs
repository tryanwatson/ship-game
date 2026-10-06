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

    /// <summary>At 1 or more, a broadside fires from both sides at once, and both reload (base 0).</summary>
    BothSides,

    /// <summary>Fraction a hit slows the ship it strikes, for a few seconds (base 0).</summary>
    SlowOnHit,

    /// <summary>At 1 or more, shots fly over land (base 0).</summary>
    IgnoresLand,

    /// <summary>Times a shot that strikes a ship bounces on to the next enemy nearby (base 0).</summary>
    Ricochets,

    /// <summary>Shells in a line walked from the ship to the aim point, in place of a salvo (base 0: none).</summary>
    CarpetShells,

    /// <summary>
    /// Seconds the fires a weapon starts burn (base 0: it starts none): where a mortar shell lands, where a broadside
    /// ball strikes a ship, and all along a long gun shot's path.
    /// </summary>
    FireSeconds,

    /// <summary>Damage a second to anything in one of those fires.</summary>
    FireDps,

    /// <summary>Degrees fore or aft of the beam a broadside can be laid (base <see cref="BroadsideVolley.AimArcDegrees"/>).</summary>
    AimArc,

    /// <summary>At 1 or more, the broadside fires a ring all round the ship, by itself, whenever an enemy's in range (base 0).</summary>
    Ring,

    /// <summary>At 1 or more, each broadside deck fires by itself whenever it's loaded and an enemy's in its lane (base 0).</summary>
    AutoFire,

    /// <summary>Extra balls of grape each cannon fires, fanned out, each at <see cref="BroadsideVolley.GrapeDamageFraction"/> (base 0).</summary>
    Grapeshot,

    /// <summary>Times a ball skips on, off the water at the end of its flight or off a ship it strikes (base 0).</summary>
    Skips,

    /// <summary>Fraction of the reload every hit gives back to the weapon that fired it (base 0).</summary>
    HitRefund,

    /// <summary>Fraction of the reload a hit that sinks its target gives back (base 0).</summary>
    KillRefund,

    /// <summary>Times a hit splits in two, each half flying on to another enemy nearby (base 0).</summary>
    Forks,

    /// <summary>Radius of the burst every hit sets off (base 0: none).</summary>
    ExplosionRadius,

    /// <summary>A hit's burst, as a fraction of the shot's damage.</summary>
    ExplosionDamage,
}

/// <summary>
/// A change to one ability's numbers. Combines like <see cref="StatModifier"/>: value = (base + flat) * (1 + percent) * multipliers.
/// </summary>
/// <param name="AbilityId">The <see cref="Ability.Id"/> it applies to.</param>
/// <param name="Source">What granted it (a skill's or card's source tag), so it can be removed with its source.</param>
public readonly record struct AbilityModifier(string AbilityId, AbilityStat Stat, ModifierKind Kind, float Value, string Source);
