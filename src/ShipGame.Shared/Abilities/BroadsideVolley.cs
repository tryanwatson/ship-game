using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Abilities;

/// <summary>Fires a row of cannonballs straight out of one side of the ship.</summary>
public sealed class BroadsideVolley : Ability
{
    public const int CannonCount = 4;

    // Base values; the firing ship's stats scale them (see DamageFor / RangeFor / ProjectileSpeedFor).
    public const float Damage = 10f;
    public const float Range = 8f;
    public const float ProjectileSpeed = 14f;

    public static float DamageFor(Ship ship) => Damage * ship.Stats.WeaponDamage;

    public static float RangeFor(Ship ship) => Range * ship.Stats.WeaponRange;

    public static float ProjectileSpeedFor(Ship ship) => ProjectileSpeed * ship.Stats.ProjectileSpeed;

    // Fraction of the hull length that the row of cannons spans.
    private const float CannonSpanFraction = 0.6f;

    public BroadsideVolley(BroadsideSide side)
    {
        if (side == BroadsideSide.None)
            throw new ArgumentException("A volley must fire to port or starboard.", nameof(side));
        Side = side;
    }

    public BroadsideSide Side { get; }

    public override string Id => Side == BroadsideSide.Port ? "volley-port" : "volley-starboard";

    public override string Name => Side == BroadsideSide.Port ? "Port Volley" : "Starboard Volley";

    public override int CooldownTicks => (int)(2.5f * SimConstants.TickRate);

    public override bool Cast(World world, Ship caster, Vector2 target)
    {
        var forward = caster.Forward;
        var outward = FiringDirection(caster);
        var halfSpan = HalfSpan(caster);
        var speed = ProjectileSpeedFor(caster);
        var damage = DamageFor(caster);
        var lifetimeTicks = (int)MathF.Ceiling(RangeFor(caster) / speed * SimConstants.TickRate);

        // Cannonballs inherit the ship's motion, so firing on the move leads the shot.
        var velocity = outward * speed + forward * caster.Speed;

        for (var i = 0; i < CannonCount; i++)
        {
            var along = -halfSpan + 2f * halfSpan * i / (CannonCount - 1);
            var muzzle = caster.Position + forward * along + outward * (caster.Stats.Beam / 2f);
            world.SpawnProjectile(caster, muzzle, velocity, damage, lifetimeTicks);
        }

        return true;
    }

    /// <summary>Unit vector the cannons fire along: starboard is the bow rotated +90 degrees (Y-down world).</summary>
    public Vector2 FiringDirection(Ship ship)
    {
        var f = ship.Forward;
        return Side == BroadsideSide.Starboard ? new Vector2(-f.Y, f.X) : new Vector2(f.Y, -f.X);
    }

    public static float HalfSpan(Ship ship) => ship.Stats.Length * CannonSpanFraction / 2f;

    /// <summary>
    /// Whether a circle at <paramref name="point"/> sits in this volley's firing lane, ignoring motion.
    /// Used for aiming previews; actual hits come from the projectiles.
    /// </summary>
    public bool Covers(Ship ship, Vector2 point, float radius)
    {
        var offset = point - ship.Position;
        var along = Vector2.Dot(offset, ship.Forward);
        var outward = Vector2.Dot(offset, FiringDirection(ship));
        return outward >= 0f
            && outward <= ship.Stats.Beam / 2f + RangeFor(ship) + radius
            && MathF.Abs(along) <= HalfSpan(ship) + Projectile.Radius + radius;
    }
}
