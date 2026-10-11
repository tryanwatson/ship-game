using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Simulation;

namespace ShipGame.BalanceSim;

/// <summary>
/// A stand-in player: fights like a relentless pirate (closes on the nearest enemy and fires whatever bears), and
/// sometimes sees a telegraphed shot coming and turns out of it, as a player watching the warnings would. It never
/// anchors, kites, plunders, or picks targets cleverly, so treat what it shows as a floor on a decent player.
/// </summary>
/// <param name="dodgeChance">How often it reacts to a long gun laid on it, or a shell falling where it's headed (0..1).</param>
public sealed class PlayerBot(int seed, float dodgeChance) : INpcBehavior
{
    /// <summary>A warning line or a shell's blast this close to where the ship will be counts as aimed at it.</summary>
    private const float ThreatMargin = 1.2f;

    private readonly HunterBehavior _fighter = new(Vector2.Zero, relentless: true);
    private readonly Random _rng = new(seed);
    private readonly HashSet<object> _judged = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _dodging = new(ReferenceEqualityComparer.Instance);
    private int _side;

    public void Update(World world, Ship ship)
    {
        _fighter.Update(world, ship);
        if (Threat(world, ship) is not { } away)
            return;
        // Hard over, away from it, under full sail.
        var desired = MathF.Atan2(away.Y, away.X);
        var heading = Navigation.ChooseHeading(world, ship, desired, ref _side);
        ship.MoveTarget = null;
        ship.Throttle = ShipMovement.ThrottleLevels;
        ship.Rudder = MathF.Sign(Angles.Delta(ship.Heading, heading));
    }

    /// <summary>Which way to go to get clear of a shot or shell it has decided to dodge; null if none is coming.</summary>
    private Vector2? Threat(World world, Ship ship)
    {
        foreach (var warning in world.Warnings)
        {
            if (world.FindShip(warning.ShipId) is not { } shooter || shooter.Team == ship.Team
                || shooter.GetAbility(warning.Slot)?.Definition is not LongGun)
                continue;
            var seconds = (warning.FireTick - world.Tick) / (float)SimConstants.TickRate
                          + Vector2.Distance(shooter.Position, ship.Position) / LongGun.SpeedFor(shooter);
            var then = ship.Position + ship.Velocity * seconds;
            var line = warning.Target - shooter.Position;
            if (line.LengthSquared() < 1e-4f)
                continue;
            var along = Vector2.Normalize(line);
            var offset = then - shooter.Position;
            var across = offset - along * Vector2.Dot(offset, along);
            if (across.Length() <= ship.Stats.Length / 2f + ThreatMargin && Reacts(warning))
                return across.LengthSquared() > 1e-4f ? Vector2.Normalize(across) : new Vector2(-along.Y, along.X);
        }
        foreach (var strike in world.Strikes)
        {
            if (strike.Team == ship.Team)
                continue;
            var seconds = (strike.ImpactTick - world.Tick) / (float)SimConstants.TickRate;
            var then = ship.Position + ship.Velocity * seconds;
            var off = then - strike.Target;
            if (off.Length() <= strike.Radius + ship.Stats.Length / 2f + ThreatMargin && Reacts(strike))
                return off.LengthSquared() > 1e-4f ? Vector2.Normalize(off) : ship.Forward;
        }
        return null;
    }

    /// <summary>Whether it sees this one coming: decided once a threat, and stuck to.</summary>
    private bool Reacts(object threat)
    {
        if (_judged.Add(threat) && _rng.NextSingle() < dodgeChance)
            _dodging.Add(threat);
        return _dodging.Contains(threat);
    }
}
