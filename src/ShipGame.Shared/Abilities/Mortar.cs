using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Abilities;

/// <summary>
/// Area strike: lob a shell at a point up to <see cref="Range"/> away. It flies over everything (islands included)
/// and bursts after a flight time that grows with distance, hurting every hostile ship within
/// <see cref="BlastRadius"/>. The landing spot is public while the shell is in the air, so it can be dodged.
/// </summary>
public sealed class Mortar : Ability
{
    public const float Damage = 35f;
    public const float Range = 30f;
    public const float BlastRadius = 2.5f;

    // Flight time = MinFlightSeconds + distance / ShellSpeed: even a point-blank shot gives a moment's warning.
    public const float MinFlightSeconds = 0.5f;
    public const float ShellSpeed = 25f;

    public override string Id => "mortar";

    public override string Name => "Mortar";

    public override bool IsAimed => true;

    public override int CooldownTicks => (int)(12f * SimConstants.TickRate);

    public static float RangeFor(Ship ship) => Range * ship.Stats.WeaponRange;

    /// <summary>Where the shell will land: the aim point, pulled in to the mortar's range.</summary>
    public static Vector2 LandingPoint(Ship ship, Vector2 aim)
    {
        if (!float.IsFinite(aim.X) || !float.IsFinite(aim.Y))
            return ship.Position;
        var offset = aim - ship.Position;
        var range = RangeFor(ship);
        return offset.Length() <= range ? aim : ship.Position + Vector2.Normalize(offset) * range;
    }

    public static int FlightTicks(Ship ship, float distance) =>
        Math.Max(1, (int)MathF.Ceiling((MinFlightSeconds + distance / (ShellSpeed * ship.Stats.ProjectileSpeed)) * SimConstants.TickRate));

    public override bool Cast(World world, Ship caster, Vector2 target)
    {
        var landing = LandingPoint(caster, target);
        var flight = FlightTicks(caster, Vector2.Distance(caster.Position, landing));
        world.LaunchStrike(caster, landing, BlastRadius, Damage * caster.Stats.WeaponDamage, flight);
        return true;
    }
}
