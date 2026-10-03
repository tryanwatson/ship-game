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

    public const float Damage = 22f;
    public const float Range = 16f;
    public const float ProjectileSpeed = 26f;
    public const float ShotRadius = 0.3f;

    /// <summary>Hits at least this fraction of the gun's reach from the muzzle count as long range.</summary>
    public const float LongRangeFraction = 0.6f;

    public override string Id => AbilityId;

    public override string Name => "Long Gun";

    public override string Description => "ONE HEAVY AIMED SHOT AT LONG RANGE, ANY DIRECTION.";

    public override bool IsAimed => true;

    public override int CooldownTicks => (int)(5f * SimConstants.TickRate);

    public static float RangeFor(Ship ship) => Range * ship.Stats.WeaponRange * ship.AbilityValue(AbilityId, AbilityStat.Range, 1f);

    public static float SpeedFor(Ship ship) =>
        ProjectileSpeed * ship.Stats.ProjectileSpeed * ship.AbilityValue(AbilityId, AbilityStat.ProjectileSpeed, 1f);

    public static float DamageFor(Ship ship) => Damage * ship.Stats.WeaponDamage * ship.AbilityValue(AbilityId, AbilityStat.Damage, 1f);

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

    public override bool Cast(World world, Ship caster, Vector2 target)
    {
        var direction = AimDirection(caster, target);
        var speed = SpeedFor(caster);
        var lifetimeTicks = (int)MathF.Ceiling(RangeFor(caster) / speed * SimConstants.TickRate);
        var muzzle = caster.Position + direction * (caster.Stats.Beam / 2f);
        var effects = new ShotEffects
        {
            AbilityId = Id,
            Pierce = (int)MathF.Round(caster.AbilityValue(Id, AbilityStat.Pierce, 0f)),
            LongRange = LongRangeFor(caster),
            LongDamageBonus = caster.AbilityValue(Id, AbilityStat.LongRangeDamage, 0f),
            LongRangeRefund = caster.AbilityValue(Id, AbilityStat.LongRangeRefund, 0f),
        };
        world.SpawnProjectile(caster, muzzle, direction * speed, DamageFor(caster), lifetimeTicks, ShotRadius, effects);
        return true;
    }
}
