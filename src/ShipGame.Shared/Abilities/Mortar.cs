using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Abilities;

/// <summary>
/// Area strike: lob a shell at a point up to <see cref="Range"/> away. It flies over everything (islands included)
/// and bursts after a flight time that grows with distance, hurting every hostile ship within
/// <see cref="BlastRadius"/>. The landing spot is public while the shell is in the air, so it can be dodged.
/// Skills can make it a salvo (<see cref="AbilityStat.ShotCount"/>: the extra shells land in sequence around the
/// aim point) or scatter bomblets on impact (<see cref="AbilityStat.ClusterCount"/>).
/// </summary>
public sealed class Mortar : Ability
{
    public const string AbilityId = "mortar";

    public const float Damage = 35f;
    public const float Range = 18f;
    public const float BlastRadius = 2.5f;

    // Flight time = MinFlightSeconds + distance / ShellSpeed: even a point-blank shot gives a moment's warning.
    public const float MinFlightSeconds = 0.5f;
    public const float ShellSpeed = 25f;

    /// <summary>
    /// Pirates' shells fly this much longer and burst this much smaller than a player's, so a player who sees the
    /// landing spot in time can sail out from under it. Applied to what the server sends, so clients just follow.
    /// </summary>
    public const float PirateFlightTimeScale = 1.5f;
    public const float PirateBlastRadiusScale = 0.8f;

    /// <summary>In a salvo, each shell after the first lands this much later, this far from the aim point.</summary>
    public const int SalvoGapTicks = 6;
    public const float SalvoSpread = 1.5f;

    // Bomblets: a ring just outside the blast, each a fraction of the shell, going off a moment after it.
    public const float ClusterSpreadFraction = 1.1f;
    public const float ClusterRadiusFraction = 0.5f;
    public const float ClusterDamageFraction = 0.25f;
    public const int ClusterDelayTicks = 8;

    public override string Id => AbilityId;

    public override string Name => "Mortar";

    public override string Description => "LOB A SHELL ANYWHERE IN RANGE. IT BURSTS ON EVERY SHIP NEAR WHERE IT LANDS.";

    public override bool IsAimed => true;

    public override int CooldownTicks => (int)(6f * SimConstants.TickRate);

    public static float RangeFor(Ship ship) => Range * ship.Stats.WeaponRange * ship.AbilityValue(AbilityId, AbilityStat.Range, 1f);

    public static float BlastRadiusFor(Ship ship) =>
        ship.AbilityValue(AbilityId, AbilityStat.BlastRadius, BlastRadius) * (ship.Team == Team.Pirates ? PirateBlastRadiusScale : 1f);

    public static float DamageFor(Ship ship) =>
        Damage * ship.Stats.WeaponDamage * ship.AbilityValue(AbilityId, AbilityStat.Damage, 1f) * ship.CastDamageScale;

    /// <summary>Carpet Bombing: shells in a line from the ship to the aim point, in place of a salvo; 0 without it.</summary>
    public static int CarpetShellsFor(Ship ship) => (int)MathF.Round(ship.AbilityValue(AbilityId, AbilityStat.CarpetShells, 0f));

    /// <summary>A carpet's first shell lands this far out from the ship, and each after it a few ticks later than its flight alone.</summary>
    public const float CarpetStart = 4f;
    public const int CarpetGapTicks = 2;

    /// <summary>
    /// The most blasts one cast sets off, shells and their bomblets together: past it, bomblets are fewer and hit
    /// harder, so the sim and the network carry hundreds of blasts a cast, not thousands.
    /// </summary>
    public const int MaxStrikesPerCast = 400;

    public static int ShellCountFor(Ship ship) => CarpetShellsFor(ship) is > 1 and var carpet ? carpet : SalvoShellsFor(ship);

    private static int SalvoShellsFor(Ship ship) =>
        Math.Max(1, (int)MathF.Round(ship.AbilityValue(AbilityId, AbilityStat.ShotCount, 1f)));

    /// <summary>Where the shell will land: the aim point, pulled in to the mortar's range.</summary>
    public static Vector2 LandingPoint(Ship ship, Vector2 aim)
    {
        if (!float.IsFinite(aim.X) || !float.IsFinite(aim.Y))
            return ship.Position;
        var offset = aim - ship.Position;
        var range = RangeFor(ship);
        return offset.Length() <= range ? aim : ship.Position + Vector2.Normalize(offset) * range;
    }

    public static int FlightTicks(Ship ship, float distance)
    {
        var seconds = (MinFlightSeconds + distance / (ShellSpeed * ship.Stats.ProjectileSpeed))
                      * ship.AbilityValue(AbilityId, AbilityStat.FlightTime, 1f)
                      * (ship.Team == Team.Pirates ? PirateFlightTimeScale : 1f);
        return Math.Max(1, (int)MathF.Ceiling(seconds * SimConstants.TickRate));
    }

    /// <summary>A salvo shell's landing point, shared by firing and the aiming preview.</summary>
    public static Vector2 ShellLandingPoint(Ship ship, Vector2 aim, int shellIndex)
    {
        var shells = ShellCountFor(ship);
        if (shellIndex < 0 || shellIndex >= shells)
            throw new ArgumentOutOfRangeException(nameof(shellIndex));
        var landing = LandingPoint(ship, aim);
        if (CarpetShellsFor(ship) > 1)
        {
            // Walked out from just off the ship to the aim point, evenly.
            var offset = landing - ship.Position;
            var start = offset.LengthSquared() > CarpetStart * CarpetStart
                ? ship.Position + Vector2.Normalize(offset) * CarpetStart
                : ship.Position + offset * 0.5f;
            return Vector2.Lerp(start, landing, shellIndex / (float)(shells - 1));
        }
        if (shellIndex == 0)
            return landing;
        var angle = MathF.Tau * (shellIndex - 1) / (shells - 1) + ship.Heading;
        return landing + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * SalvoSpread;
    }

    public override bool Cast(World world, Ship caster, Vector2 target)
    {
        var landing = LandingPoint(caster, target);
        var flight = FlightTicks(caster, Vector2.Distance(caster.Position, landing));
        var radius = BlastRadiusFor(caster);
        var damage = DamageFor(caster);

        // As many bomblets a shell as the cast can throw (see MaxStrikesPerCast); any more go into those it does.
        var shells = ShellCountFor(caster);
        var wanted = (int)MathF.Round(caster.AbilityValue(Id, AbilityStat.ClusterCount, 0f));
        var bomblets = Math.Clamp(MaxStrikesPerCast / shells - 1, 0, wanted);
        var cluster = bomblets > 0
            ? new ClusterEffect(bomblets, radius * ClusterSpreadFraction, radius * ClusterRadiusFraction,
                ClusterDamageFraction * wanted / bomblets, ClusterDelayTicks)
            : null;

        var fire = FireFor(caster, Id);

        // A salvo: the first shell on the aim point, the rest round it, one after another. A carpet: shells walking out
        // from the ship to the aim point, each landing a little after the one before.
        var carpet = CarpetShellsFor(caster) > 1;
        for (var i = 0; i < shells; i++)
        {
            var point = ShellLandingPoint(caster, target, i);
            var ticks = carpet ? FlightTicks(caster, Vector2.Distance(caster.Position, point)) + i * CarpetGapTicks : flight + i * SalvoGapTicks;
            world.LaunchStrike(caster, point, radius, damage, ticks, cluster, fire);
        }
        return true;
    }
}
