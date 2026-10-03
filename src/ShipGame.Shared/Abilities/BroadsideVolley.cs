using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Abilities;

/// <summary>
/// The broadside: one ability for both gun decks. Each cast fires a row of cannonballs straight out of whichever
/// side the aim point is on. The two decks reload independently (cooldown channels <see cref="PortChannel"/> and
/// <see cref="StarboardChannel"/>), so alternating sides keeps the guns working.
/// </summary>
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

    public override string Id => "broadside";

    public override string Name => "Broadside";

    public const int PortChannel = 0;
    public const int StarboardChannel = 1;

    /// <summary>Per side.</summary>
    public override int CooldownTicks => (int)(2.5f * SimConstants.TickRate);

    public override bool IsAimed => true;

    public override int CooldownChannels => 2;

    public override int ChannelFor(Ship caster, Vector2 target) => ChannelOf(SideToward(caster, target));

    public static int ChannelOf(BroadsideSide side) => side == BroadsideSide.Starboard ? StarboardChannel : PortChannel;

    /// <summary>The side facing <paramref name="aim"/>: starboard if it's to the right of the bow (Y-down world), else port.</summary>
    public static BroadsideSide SideToward(Ship ship, Vector2 aim)
    {
        var offset = aim - ship.Position;
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y))
            return BroadsideSide.Starboard;
        return Geometry.Cross(ship.Forward, offset) >= 0f ? BroadsideSide.Starboard : BroadsideSide.Port;
    }

    public override bool Cast(World world, Ship caster, Vector2 target)
    {
        var side = SideToward(caster, target);
        var forward = caster.Forward;
        var outward = FiringDirection(caster, side);
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

    /// <summary>Unit vector a side's guns fire along: starboard is the bow rotated +90 degrees (Y-down world).</summary>
    public static Vector2 FiringDirection(Ship ship, BroadsideSide side)
    {
        var f = ship.Forward;
        return side == BroadsideSide.Starboard ? new Vector2(-f.Y, f.X) : new Vector2(f.Y, -f.X);
    }

    public static float HalfSpan(Ship ship) => ship.Stats.Length * CannonSpanFraction / 2f;

    /// <summary>
    /// Whether a circle at <paramref name="point"/> sits in one side's firing lane, ignoring motion.
    /// Used for aiming previews and AI; actual hits come from the projectiles.
    /// </summary>
    public static bool Covers(Ship ship, BroadsideSide side, Vector2 point, float radius)
    {
        var offset = point - ship.Position;
        var along = Vector2.Dot(offset, ship.Forward);
        var outward = Vector2.Dot(offset, FiringDirection(ship, side));
        return outward >= 0f
            && outward <= ship.Stats.Beam / 2f + RangeFor(ship) + radius
            && MathF.Abs(along) <= HalfSpan(ship) + Projectile.DefaultRadius + radius;
    }

    /// <summary>The side whose lane covers <paramref name="point"/>, or <see cref="BroadsideSide.None"/>.</summary>
    public static BroadsideSide SideCovering(Ship ship, Vector2 point, float radius) =>
        Covers(ship, BroadsideSide.Starboard, point, radius) ? BroadsideSide.Starboard
        : Covers(ship, BroadsideSide.Port, point, radius) ? BroadsideSide.Port
        : BroadsideSide.None;
}
