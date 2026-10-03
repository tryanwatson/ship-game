using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Abilities;

/// <summary>
/// Skillshot: one fast, heavy ball fired from the ship toward the aim point, in any direction. It hits the first
/// ship it meets (or land) and is gone. Doesn't inherit the ship's motion, so where you aim is where it goes.
/// </summary>
public sealed class LongGun : Ability
{
    public const float Damage = 22f;
    public const float Range = 16f;
    public const float ProjectileSpeed = 26f;
    public const float ShotRadius = 0.3f;

    public override string Id => "long-gun";

    public override string Name => "Long Gun";

    public override bool IsAimed => true;

    public override int CooldownTicks => (int)(5f * SimConstants.TickRate);

    public static float RangeFor(Ship ship) => Range * ship.Stats.WeaponRange;

    public static float SpeedFor(Ship ship) => ProjectileSpeed * ship.Stats.ProjectileSpeed;

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
        world.SpawnProjectile(caster, muzzle, direction * speed, Damage * caster.Stats.WeaponDamage, lifetimeTicks, ShotRadius);
        return true;
    }
}
