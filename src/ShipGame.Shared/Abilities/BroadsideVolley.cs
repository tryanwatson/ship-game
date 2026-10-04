using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Abilities;

/// <summary>
/// The broadside: one ability for both gun decks. Each cast fires a row of cannonballs out of whichever side the aim
/// point is on, laid toward it but never more than <see cref="AimArcFor"/> fore or aft of the beam (a skill can widen
/// that with <see cref="AbilityStat.AimArc"/>). The two decks reload independently (cooldown channels <see cref="PortChannel"/> and
/// <see cref="StarboardChannel"/>), so alternating sides keeps the guns working.
/// Skills can change the number of cannon, and add damage up close (<see cref="AbilityStat.CloseRangeDamage"/>)
/// or when fired near full sail (<see cref="AbilityStat.SpeedDamage"/>).
/// </summary>
public sealed class BroadsideVolley : Ability
{
    public const string AbilityId = "broadside";

    public const int CannonCount = 4;

    // Base values; the firing ship's stats and skills scale them (see DamageFor / RangeFor / ProjectileSpeedFor).
    public const float Damage = 10f;
    public const float Range = 8f;
    public const float ProjectileSpeed = 14f;

    /// <summary>Hits within this fraction of the range count as close (for <see cref="AbilityStat.CloseRangeDamage"/>).</summary>
    public const float CloseRangeFraction = 0.5f;

    /// <summary>Fraction of top speed the ship must be making for <see cref="AbilityStat.SpeedDamage"/>.</summary>
    public const float RunningSpeedFraction = 0.75f;

    /// <summary>How far fore or aft of the beam the guns can be laid, in degrees: a 30-degree window each side.</summary>
    public const float AimArcDegrees = 15f;

    /// <summary>However far skills widen the window, the guns still fire off the side.</summary>
    public const float MaxAimArcDegrees = 75f;

    /// <summary>This ship's aiming window either side of the beam, in radians.</summary>
    public static float AimArcFor(Ship ship) =>
        Math.Clamp(ship.AbilityValue(AbilityId, AbilityStat.AimArc, AimArcDegrees), 0f, MaxAimArcDegrees) * MathF.PI / 180f;

    public static float DamageFor(Ship ship) =>
        Damage * ship.Stats.WeaponDamage * ship.AbilityValue(AbilityId, AbilityStat.Damage, 1f) * ship.CastDamageScale;

    /// <summary>Twin Decks: every broadside goes off from both sides.</summary>
    public static bool FiresBothSides(Ship ship) => ship.AbilityValue(AbilityId, AbilityStat.BothSides, 0f) >= 0.5f;

    public static float RangeFor(Ship ship) => Range * ship.Stats.WeaponRange * ship.AbilityValue(AbilityId, AbilityStat.Range, 1f);

    public static float ProjectileSpeedFor(Ship ship) =>
        ProjectileSpeed * ship.Stats.ProjectileSpeed * ship.AbilityValue(AbilityId, AbilityStat.ProjectileSpeed, 1f);

    public static int CannonCountFor(Ship ship) =>
        Math.Max(1, (int)MathF.Round(ship.AbilityValue(AbilityId, AbilityStat.ShotCount, CannonCount)));

    /// <summary>Whether the ship is moving fast enough for running-guns damage.</summary>
    public static bool IsRunning(Ship ship) => ship.Speed >= ship.Stats.MaxSpeed * RunningSpeedFraction;

    // Fraction of the hull length that the row of cannons spans.
    private const float CannonSpanFraction = 0.6f;

    public override string Id => AbilityId;

    public override string Name => "Broadside";

    public override string Description => "A ROW OF CANNON OFF EITHER BEAM, LAID A LITTLE FORE OR AFT. EACH SIDE RELOADS ON ITS OWN.";

    public const int PortChannel = 0;
    public const int StarboardChannel = 1;

    /// <summary>
    /// A pirate's broadside lights up the lane it's about to fire down this long before it fires (see
    /// <see cref="ShotWarning"/>): short, since the lane is wide and close. Players' guns fire at once.
    /// </summary>
    public const float PirateWindupSeconds = 0.5f;
    public static readonly int PirateWindupTicks = (int)MathF.Round(PirateWindupSeconds * SimConstants.TickRate);

    public override int WindupTicksFor(Ship caster) => WindupTicks(caster);

    public static int WindupTicks(Ship ship) => ship.Team == Team.Pirates ? PirateWindupTicks : 0;

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
        var offset = AimOffset(caster, caster.Position, caster.Heading, side, target);
        Fire(world, caster, side, offset);
        if (FiresBothSides(caster))
        {
            // The other deck goes off too, laid as far fore or aft, and reloads with it (not for an echo, which reloads nothing).
            var other = side == BroadsideSide.Starboard ? BroadsideSide.Port : BroadsideSide.Starboard;
            Fire(world, caster, other, -offset);
            if (!caster.IsEchoing)
                caster.FindAbility(Id)?.StartCooldown(ChannelOf(other), CooldownTicksFor(caster), caster.Stats.CooldownSpeed);
        }
        return true;
    }

    /// <summary>The side that was lit up fires, whichever way the ship has turned since, laid as near the target as its arc allows.</summary>
    public override bool CastWarned(World world, Ship caster, ShotWarning warning)
    {
        var side = warning.Channel == StarboardChannel ? BroadsideSide.Starboard : BroadsideSide.Port;
        return Fire(world, caster, side, AimOffset(caster, caster.Position, caster.Heading, side, warning.Target));
    }

    /// <summary>Fires one side's guns, laid <paramref name="offset"/> radians off its beam (see <see cref="AimOffset"/>).</summary>
    private bool Fire(World world, Ship caster, BroadsideSide side, float offset)
    {
        var forward = caster.Forward;
        var outward = FiringDirection(caster, side);
        var direction = DirectionAt(caster.Heading, side, offset);
        var halfSpan = HalfSpan(caster);
        var speed = ProjectileSpeedFor(caster);
        var range = RangeFor(caster);
        var damage = DamageFor(caster);
        if (IsRunning(caster))
            damage *= 1f + caster.AbilityValue(Id, AbilityStat.SpeedDamage, 0f);
        var lifetimeTicks = (int)MathF.Ceiling(range / speed * SimConstants.TickRate);
        var effects = new ShotEffects
        {
            AbilityId = Id,
            CloseRange = range * CloseRangeFraction,
            CloseDamageBonus = caster.AbilityValue(Id, AbilityStat.CloseRangeDamage, 0f),
            SlowOnHit = caster.AbilityValue(Id, AbilityStat.SlowOnHit, 0f),
        };

        // Cannonballs inherit the ship's motion, so firing on the move leads the shot.
        var velocity = direction * speed + forward * caster.Speed;

        var cannons = CannonCountFor(caster);
        for (var i = 0; i < cannons; i++)
        {
            var along = cannons == 1 ? 0f : -halfSpan + 2f * halfSpan * i / (cannons - 1);
            var muzzle = caster.Position + forward * along + outward * (caster.Stats.Beam / 2f);
            world.SpawnProjectile(caster, muzzle, velocity, damage, lifetimeTicks, effects: effects);
        }

        return true;
    }

    /// <summary>Unit vector a side's guns fire along: starboard is the bow rotated +90 degrees (Y-down world).</summary>
    public static Vector2 FiringDirection(Ship ship, BroadsideSide side)
    {
        var f = ship.Forward;
        return side == BroadsideSide.Starboard ? new Vector2(-f.Y, f.X) : new Vector2(f.Y, -f.X);
    }

    /// <summary>
    /// How far off <paramref name="side"/>'s beam its guns are laid to aim at <paramref name="aim"/> from a ship at
    /// <paramref name="position"/> facing <paramref name="heading"/>: radians, positive turning clockwise (Y-down),
    /// held within the ship's <see cref="AimArcFor"/>. Straight abeam if there's no usable aim.
    /// </summary>
    public static float AimOffset(Ship ship, Vector2 position, float heading, BroadsideSide side, Vector2 aim)
    {
        var toAim = aim - position;
        if (!float.IsFinite(toAim.X) || !float.IsFinite(toAim.Y) || toAim.LengthSquared() < 1e-6f)
            return 0f;
        var arc = AimArcFor(ship);
        return Math.Clamp(Angles.Delta(BeamAngle(heading, side), MathF.Atan2(toAim.Y, toAim.X)), -arc, arc);
    }

    /// <summary>Unit vector the guns fire along, laid <paramref name="offset"/> radians off <paramref name="side"/>'s beam.</summary>
    public static Vector2 DirectionAt(float heading, BroadsideSide side, float offset)
    {
        var angle = BeamAngle(heading, side) + offset;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    // Starboard is the bow rotated +90 degrees (Y-down world), port -90.
    private static float BeamAngle(float heading, BroadsideSide side) =>
        heading + (side == BroadsideSide.Starboard ? MathF.PI / 2f : -MathF.PI / 2f);

    public static float HalfSpan(Ship ship) => ship.Stats.Length * CannonSpanFraction / 2f;

    /// <summary>
    /// Whether a circle at <paramref name="point"/> sits in one side's firing lane, laid toward it, ignoring motion.
    /// Used for aiming previews and AI; actual hits come from the projectiles.
    /// </summary>
    public static bool Covers(Ship ship, BroadsideSide side, Vector2 point, float radius) =>
        Covers(ship, ship.Position, ship.Heading, side, point, radius);

    /// <summary>As <see cref="Covers(Ship, BroadsideSide, Vector2, float)"/>, with the ship at another position and heading.</summary>
    public static bool Covers(Ship ship, Vector2 position, float heading, BroadsideSide side, Vector2 point, float radius)
    {
        var forward = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
        var right = new Vector2(-forward.Y, forward.X);
        var outward = side == BroadsideSide.Starboard ? right : -right;
        if (Vector2.Dot(point - position, outward) < 0f)
            return false;

        // The lane is a parallelogram: the row of muzzles along the hull, swept out along the direction it's laid.
        // Put the point in those terms: how far along the hull (along) and how far down the lane (down).
        var direction = DirectionAt(heading, side, AimOffset(ship, position, heading, side, point));
        var fromMuzzles = point - (position + outward * (ship.Stats.Beam / 2f));
        var skew = Geometry.Cross(forward, direction); // never near 0: the arc keeps the guns off the bow and stern
        var down = Geometry.Cross(forward, fromMuzzles) / skew;
        var along = Geometry.Cross(fromMuzzles, direction) / skew;
        return down <= RangeFor(ship) + radius
            && MathF.Abs(along) <= HalfSpan(ship) + (Projectile.DefaultRadius + radius) / MathF.Abs(skew);
    }

    /// <summary>The side whose lane covers <paramref name="point"/>, or <see cref="BroadsideSide.None"/>.</summary>
    public static BroadsideSide SideCovering(Ship ship, Vector2 point, float radius) =>
        SideCovering(ship, ship.Position, ship.Heading, point, radius);

    /// <summary>As <see cref="SideCovering(Ship, Vector2, float)"/>, with the ship at another position and heading.</summary>
    public static BroadsideSide SideCovering(Ship ship, Vector2 position, float heading, Vector2 point, float radius) =>
        Covers(ship, position, heading, BroadsideSide.Starboard, point, radius) ? BroadsideSide.Starboard
        : Covers(ship, position, heading, BroadsideSide.Port, point, radius) ? BroadsideSide.Port
        : BroadsideSide.None;
}
