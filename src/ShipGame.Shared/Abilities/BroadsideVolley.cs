using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Abilities;

/// <summary>
/// The broadside: one ability for both gun decks. Each cast fires a row of cannonballs out of whichever side the aim
/// point is on, laid toward it but never more than <see cref="AimArcFor"/> fore or aft of the beam (a skill can widen
/// that with <see cref="AbilityStat.AimArc"/>). The two decks reload independently (cooldown channels <see cref="PortChannel"/> and
/// <see cref="StarboardChannel"/>), so alternating sides keeps the guns working.
/// Skills can change the number of cannon, and add damage up close (<see cref="AbilityStat.CloseRangeDamage"/>)
/// or when fired near full sail (<see cref="AbilityStat.SpeedDamage"/>). Cards can turn it into a ring all round the
/// ship (<see cref="AbilityStat.Ring"/>), and make it fire by itself (<see cref="AbilityStat.AutoFire"/>, and the ring
/// always does): see <see cref="World"/>'s FireBroadsidesByThemselves.
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

    /// <summary>Man o' War: in a ring, every gun fires this many ways.</summary>
    public const int RingShotsPerCannon = 3;

    /// <summary>Grapeshot: a cannon's balls of grape fan out this many degrees either side of its aim, each this share of a ball's damage.</summary>
    public const float GrapeSpreadDegrees = 6f;
    public const float GrapeDamageFraction = 0.6f;

    /// <summary>Skip Shot: every skip carries a ball on for this share of its flight again, at most this many times.</summary>
    public const float SkipFlightFraction = 0.6f;
    public const int MaxSkips = 5;

    /// <summary>Man o' War: the broadside is a ring all round the ship, and fires by itself.</summary>
    public static bool FiresRing(Ship ship) => ship.AbilityValue(AbilityId, AbilityStat.Ring, 0f) >= 0.5f;

    /// <summary>Gun Captains: each deck fires by itself when there's an enemy in its lane.</summary>
    public static bool FiresItself(Ship ship) => ship.AbilityValue(AbilityId, AbilityStat.AutoFire, 0f) >= 0.5f;

    /// <summary>Balls each cannon fires: 1, or more with Grapeshot.</summary>
    public static int GrapeFor(Ship ship) => 1 + Math.Max(0, (int)MathF.Round(ship.AbilityValue(AbilityId, AbilityStat.Grapeshot, 0f)));

    /// <summary>
    /// The most balls one cast throws (a ring, or a side's grape). Past it, the balls it would have thrown go into
    /// those it does, as damage: the sim and the network carry a few hundred balls a broadside, not thousands.
    /// </summary>
    public const int MaxShotsPerCast = 240;

    /// <summary>Balls a Man o' War ring would throw: every gun's, every way round, both decks' with Twin Decks.</summary>
    private static int RingWorthFor(Ship ship) => CannonCountFor(ship) * RingShotsPerCannon * GrapeFor(ship) * (FiresBothSides(ship) ? 2 : 1);

    /// <summary>Balls a Man o' War ring does throw (see <see cref="MaxShotsPerCast"/>).</summary>
    public static int RingShotsFor(Ship ship) => Math.Min(RingWorthFor(ship), MaxShotsPerCast);

    /// <summary>Seconds between broadsides (each deck's, or the ring's), before any hits give reload back.</summary>
    public static float ReloadSecondsFor(Ship ship) =>
        ship.AbilityValue(AbilityId, AbilityStat.Cooldown, BaseCooldownTicks) / MathF.Max(ship.Stats.CooldownSpeed, 0.01f) / SimConstants.TickRate;

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
    public static readonly int BaseCooldownTicks = (int)(2.5f * SimConstants.TickRate);

    public override int CooldownTicks => BaseCooldownTicks;

    public override bool IsAimed => true;

    public override int CooldownChannels => 2;

    /// <summary>A ring fires from (and reloads) both decks, counted on the port one.</summary>
    public override int ChannelFor(Ship caster, Vector2 target) => FiresRing(caster) ? PortChannel : ChannelOf(SideToward(caster, target));

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
        if (FiresRing(caster))
        {
            FireRing(world, caster);
            if (!caster.IsEchoing)
                caster.FindAbility(Id)?.StartCooldown(StarboardChannel, CooldownTicksFor(caster), caster.Stats.CooldownSpeed);
            return true;
        }

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
        if (FiresRing(caster))
            return FireRing(world, caster);
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
        var (speed, lifetimeTicks, damage, effects) = Loading(caster);

        // Grapeshot: each cannon fans its balls out either side of its aim, as many as a cast can throw.
        var cannons = CannonCountFor(caster);
        var grape = Math.Clamp(MaxShotsPerCast / cannons, 1, GrapeFor(caster));
        damage *= GrapeFor(caster) / (float)grape;
        var spread = GrapeSpreadDegrees * MathF.PI / 180f;
        for (var i = 0; i < cannons; i++)
        {
            var along = cannons == 1 ? 0f : -halfSpan + 2f * halfSpan * i / (cannons - 1);
            var muzzle = caster.Position + forward * along + outward * (caster.Stats.Beam / 2f);
            for (var g = 0; g < grape; g++)
            {
                var fan = grape == 1 ? 0f : -spread + 2f * spread * g / (grape - 1);
                // Cannonballs inherit the ship's motion, so firing on the move leads the shot.
                var velocity = Geometry.Rotate(direction, fan) * speed + forward * caster.Speed;
                world.SpawnProjectile(caster, muzzle, velocity, damage, lifetimeTicks, effects: effects);
            }
        }

        return true;
    }

    /// <summary>
    /// Man o' War: every gun at once, all the way round, evenly (see <see cref="RingShotsFor"/>). An echo's ring is
    /// turned half a step, so it fills the gaps in the first.
    /// </summary>
    private bool FireRing(World world, Ship caster)
    {
        var (speed, lifetimeTicks, damage, effects) = Loading(caster);
        var shots = RingShotsFor(caster);
        damage *= RingWorthFor(caster) / (float)shots;
        var turn = caster.IsEchoing ? 0.5f : 0f;
        for (var i = 0; i < shots; i++)
        {
            var angle = caster.Heading + MathF.Tau * (i + turn) / shots;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var muzzle = caster.Position + direction * (caster.Stats.Beam / 2f);
            world.SpawnProjectile(caster, muzzle, direction * speed + caster.Forward * caster.Speed, damage, lifetimeTicks, effects: effects);
        }
        return true;
    }

    /// <summary>What every ball this ship fires right now flies and hits with: its speed, flight, damage (each, grape counted) and effects.</summary>
    private (float Speed, int LifetimeTicks, float Damage, ShotEffects Effects) Loading(Ship caster)
    {
        var speed = ProjectileSpeedFor(caster);
        var range = RangeFor(caster);
        var damage = DamageFor(caster);
        if (IsRunning(caster))
            damage *= 1f + caster.AbilityValue(Id, AbilityStat.SpeedDamage, 0f);
        if (GrapeFor(caster) > 1)
            damage *= GrapeDamageFraction;
        var lifetimeTicks = (int)MathF.Ceiling(range / speed * SimConstants.TickRate);
        var effects = new ShotEffects
        {
            AbilityId = Id,
            CloseRange = range * CloseRangeFraction,
            CloseDamageBonus = caster.AbilityValue(Id, AbilityStat.CloseRangeDamage, 0f),
            SlowOnHit = caster.AbilityValue(Id, AbilityStat.SlowOnHit, 0f),
            Skips = Math.Clamp((int)MathF.Round(caster.AbilityValue(Id, AbilityStat.Skips, 0f)), 0, MaxSkips),
            SkipTicks = Math.Max(1, (int)MathF.Round(lifetimeTicks * SkipFlightFraction)),
            HitRefund = caster.AbilityValue(Id, AbilityStat.HitRefund, 0f),
            HitFire = FireFor(caster, Id),
        };
        return (speed, lifetimeTicks, damage, effects);
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
