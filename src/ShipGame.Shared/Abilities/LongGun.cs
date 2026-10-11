using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Abilities;

/// <summary>
/// Skillshot: one fast, heavy ball fired from the ship toward the aim point, in any direction. It hits the first
/// ship it meets (or land) and is gone. Doesn't inherit the ship's motion, so where you aim is where it goes.
/// Skills can let it pierce ships (<see cref="AbilityStat.Pierce"/>), and reward hits beyond
/// <see cref="LongRangeFraction"/> of its reach with damage or a faster reload.
/// </summary>
public sealed class LongGun : Ability
{
    public const string AbilityId = "long-gun";

    public const float Damage = 30f;
    public const float Range = 16f;
    public const float ProjectileSpeed = 26f;
    public const float ShotRadius = 0.3f;

    /// <summary>Volley Gun: its shots fan out this many degrees apart.</summary>
    public const float VolleySpreadDegrees = 5f;

    /// <summary>Hits at least this fraction of the gun's reach from the muzzle count as long range.</summary>
    public const float LongRangeFraction = 0.6f;

    /// <summary>
    /// A pirate's long gun shows where it's laid this long before it fires (see <see cref="ShotWarning"/>), so a
    /// player who sees the line can turn or check out of it. Players' guns fire at once.
    /// </summary>
    public const float PirateWindupSeconds = 0.8f;
    public static readonly int PirateWindupTicks = (int)MathF.Round(PirateWindupSeconds * SimConstants.TickRate);

    /// <summary>
    /// A pirate's long gun (a sniper's, a shore battery's) hits for this share of a player's, and reloads this many times
    /// slower: the players' gun was made heavier and quicker as an opening weapon, not the shore's.
    /// </summary>
    public const float PirateDamageScale = 22f / Damage;
    public const float PirateReloadScale = 1.25f;

    public override string Id => AbilityId;

    public override string Name => "Long Gun";

    public override string Description => "ONE HEAVY AIMED SHOT AT LONG RANGE, ANY DIRECTION.";

    public override bool IsAimed => true;

    public override int CooldownTicks => (int)(4f * SimConstants.TickRate);

    public override float CooldownTicksFor(Ship ship) => base.CooldownTicksFor(ship) * (ship.Team == Team.Pirates ? PirateReloadScale : 1f);

    public override int WindupTicksFor(Ship caster) => WindupTicks(caster);

    public static int WindupTicks(Ship ship) => ship.Team == Team.Pirates ? PirateWindupTicks : 0;

    public static float RangeFor(Ship ship) => Range * ship.Stats.WeaponRange * ship.AbilityValue(AbilityId, AbilityStat.Range, 1f);

    public static float SpeedFor(Ship ship) =>
        ProjectileSpeed * ship.Stats.ProjectileSpeed * ship.AbilityValue(AbilityId, AbilityStat.ProjectileSpeed, 1f);

    public static float DamageFor(Ship ship) =>
        Damage * ship.Stats.WeaponDamage * ship.AbilityValue(AbilityId, AbilityStat.Damage, 1f) * ship.CastDamageScale
        * (ship.Team == Team.Pirates ? PirateDamageScale : 1f);

    /// <summary>Distance from the muzzle beyond which a hit counts as long range.</summary>
    public static float LongRangeFor(Ship ship) => RangeFor(ship) * LongRangeFraction;

    /// <summary>Unit direction from the ship toward <paramref name="target"/>; straight ahead if there's no usable aim.</summary>
    public static Vector2 AimDirection(Ship ship, Vector2 target)
    {
        var toTarget = target - ship.Position;
        return float.IsFinite(toTarget.X) && float.IsFinite(toTarget.Y) && toTarget.LengthSquared() > 1e-6f
            ? Vector2.Normalize(toTarget)
            : ship.Forward;
    }

    /// <summary>Shots it fires at once: 1, or a fan with Volley Gun.</summary>
    public static int ShotsFor(Ship ship) => Math.Max(1, (int)MathF.Round(ship.AbilityValue(AbilityId, AbilityStat.ShotCount, 1f)));

    public override bool Cast(World world, Ship caster, Vector2 target)
    {
        var aim = AimDirection(caster, target);
        var speed = SpeedFor(caster);
        var lifetimeTicks = (int)MathF.Ceiling(RangeFor(caster) / speed * SimConstants.TickRate);
        var effects = new ShotEffects
        {
            AbilityId = Id,
            Pierce = (int)MathF.Round(caster.AbilityValue(Id, AbilityStat.Pierce, 0f)),
            LongRange = LongRangeFor(caster),
            LongDamageBonus = caster.AbilityValue(Id, AbilityStat.LongRangeDamage, 0f),
            LongRangeRefund = caster.AbilityValue(Id, AbilityStat.LongRangeRefund, 0f),
            IgnoresLand = caster.AbilityValue(Id, AbilityStat.IgnoresLand, 0f) >= 0.5f,
            Ricochets = (int)MathF.Round(caster.AbilityValue(Id, AbilityStat.Ricochets, 0f)),
            Forks = (int)MathF.Round(caster.AbilityValue(Id, AbilityStat.Forks, 0f)),
            KillRefund = caster.AbilityValue(Id, AbilityStat.KillRefund, 0f),
            Wake = FireFor(caster, Id),
            ExplosionRadius = caster.AbilityValue(Id, AbilityStat.ExplosionRadius, 0f),
            ExplosionDamage = caster.AbilityValue(Id, AbilityStat.ExplosionDamage, 0f),
        };

        // A volley fans out evenly either side of the aim.
        var shots = ShotsFor(caster);
        var spread = VolleySpreadDegrees * MathF.PI / 180f;
        for (var i = 0; i < shots; i++)
        {
            var direction = Geometry.Rotate(aim, (i - (shots - 1) / 2f) * spread);
            var muzzle = caster.Position + direction * (caster.Stats.Beam / 2f);
            world.SpawnProjectile(caster, muzzle, direction * speed, DamageFor(caster), lifetimeTicks, ShotRadius, effects);
        }
        return true;
    }
}
