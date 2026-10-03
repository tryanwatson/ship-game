using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Ai;

public enum HunterState
{
    /// <summary>At anchor at home, watching for enemies within <see cref="HunterBehavior.AggroRange"/>.</summary>
    Guarding,

    /// <summary>Chasing and fighting a target.</summary>
    Hunting,

    /// <summary>Leashed: sailing home, ignoring enemies until it gets there.</summary>
    Returning,
}

/// <summary>
/// Aggressive pirate that guards its home. Aggroes onto enemies that come within <see cref="AggroRange"/>, then
/// chases at full sail and, once in range, steers to hold the target abeam at a comfortable distance and fires
/// whichever broadside bears. Steers by rudder, like a player on WASD. Leashed: if dragged too far from home, or
/// the target gets away, it sails home and resumes guarding.
/// </summary>
public sealed class HunterBehavior : INpcBehavior
{
    /// <summary>Enemies within this many tiles of a guarding pirate draw it out.</summary>
    public const float AggroRange = 20f;

    /// <summary>A hunting pirate gives up once its target is this far away. Above <see cref="AggroRange"/> so a
    /// player loitering at the aggro edge doesn't toggle it on and off.</summary>
    public const float DisengageRange = 30f;

    /// <summary>A hunting pirate turns for home once it's this far from it.</summary>
    public const float LeashRange = 40f;

    private const int ChaseThrottle = ShipMovement.ThrottleLevels;
    private const int EngageThrottle = 4;

    // Inside this range, stop chasing and maneuver for a broadside.
    private const float EngageRange = BroadsideVolley.Range + 2f;

    // Distance to hold the target at while engaged: well inside volley range, outside ramming distance.
    private const float PreferredRange = BroadsideVolley.Range * 0.6f;

    // How far (degrees) the helm will angle in or out from straight abeam to close or open the range.
    private const float RangeCorrectionDegrees = 45f;

    // Don't touch the rudder for heading errors smaller than this, so the helm doesn't chatter.
    private const float HeadingDeadband = 4f * MathF.PI / 180f;

    // Only fire when the predicted target center is this deep in the firing lane (fraction of hull radius).
    private const float AimTightness = 0.5f;

    // Cap on how far ahead to lead a chase, so a distant target's predicted position stays sensible.
    private const float MaxChaseLeadSeconds = 3f;

    // Which side we're presenting: +1 starboard, -1 port, 0 not yet chosen. Sticky, so the ship doesn't flip
    // sides every time the target crosses the bow.
    private int _side;

    private Ship? _target;

    // Which way we last swerved around land; see Navigation.ChooseHeading.
    private int _avoidSide;

    // Close enough to home to drop anchor.
    private const float HomeArrivalDistance = 2f;

    public HunterBehavior(Vector2 home)
    {
        Home = home;
    }

    /// <summary>Where the pirate guards from and returns to when leashed: its spawn point.</summary>
    public Vector2 Home { get; }

    public HunterState State { get; private set; } = HunterState.Guarding;

    public Ship? Target => _target;

    public void Update(World world, Ship ship)
    {
        UpdateState(world, ship);
        ship.Stance = State switch
        {
            HunterState.Guarding => NpcStance.Guarding,
            HunterState.Hunting => NpcStance.Hunting,
            _ => NpcStance.Returning,
        };
    }

    private void UpdateState(World world, Ship ship)
    {
        switch (State)
        {
            case HunterState.Guarding:
                Guard(world, ship);
                break;
            case HunterState.Hunting:
                Hunt(world, ship);
                break;
            case HunterState.Returning:
                SailHome(world, ship);
                break;
        }
    }

    private void Guard(World world, Ship ship)
    {
        var target = FindNearestEnemy(world, ship, AggroRange);
        if (target is null)
            return;

        _target = target;
        _side = 0;
        ship.IsAnchored = false;
        State = HunterState.Hunting;
        Hunt(world, ship); // engage this tick
    }

    private void StartGuarding(Ship ship)
    {
        State = HunterState.Guarding;
        _target = null;
        ship.MoveTarget = null;
        ship.Rudder = 0;
        ship.Throttle = 0;
        ship.IsAnchored = true;
    }

    private void StartReturning(Ship ship)
    {
        State = HunterState.Returning;
        _target = null;
        ship.MoveTarget = null;
        ship.Throttle = ChaseThrottle;
    }

    /// <summary>
    /// Steers home by hand rather than autopilot, so the trip goes round islands. Takes in sail once the
    /// drift will carry it the rest of the way, and anchors on arrival.
    /// </summary>
    private void SailHome(World world, Ship ship)
    {
        var toHome = Home - ship.Position;
        var distance = toHome.Length();
        if (distance <= HomeArrivalDistance)
        {
            StartGuarding(ship);
            return;
        }

        ship.Throttle = ship.Stats.StoppingDistance(ship.Speed) >= distance - HomeArrivalDistance / 2f ? 0 : ChaseThrottle;
        Steer(world, ship, MathF.Atan2(toHome.Y, toHome.X));
    }

    /// <summary>Puts the helm over toward <paramref name="desiredHeading"/>, or the nearest heading clear of land.</summary>
    private void Steer(World world, Ship ship, float desiredHeading)
    {
        var heading = Navigation.ChooseHeading(world, ship, desiredHeading, ref _avoidSide);
        var headingError = Angles.Delta(ship.Heading, heading);
        ship.Rudder = MathF.Abs(headingError) < HeadingDeadband ? 0 : MathF.Sign(headingError);
    }

    private void Hunt(World world, Ship ship)
    {
        // Lost the target (sunk): take on another one in range, or go home.
        if (_target is null || _target.IsSunk)
        {
            _target = FindNearestEnemy(world, ship, AggroRange);
            if (_target is null)
            {
                StartReturning(ship);
                return;
            }
        }

        var target = _target;
        if (Vector2.Distance(ship.Position, Home) > LeashRange
            || Vector2.Distance(ship.Position, target.Position) > DisengageRange)
        {
            StartReturning(ship);
            return;
        }

        ship.MoveTarget = null; // manual helm
        var toTarget = target.Position - ship.Position;
        var distance = toTarget.Length();
        var bearing = MathF.Atan2(toTarget.Y, toTarget.X);

        float desiredHeading;
        if (distance > EngageRange)
        {
            ship.Throttle = ChaseThrottle;
            var leadSeconds = MathF.Min(MaxChaseLeadSeconds, distance / MathF.Max(ship.Stats.MaxSpeed, 1f));
            var intercept = target.Position + target.Velocity * leadSeconds;
            desiredHeading = MathF.Atan2(intercept.Y - ship.Position.Y, intercept.X - ship.Position.X);
        }
        else
        {
            ship.Throttle = EngageThrottle;
            var relative = Angles.Delta(ship.Heading, bearing); // + is starboard
            var clearlyStarboard = relative > 0.35f && relative < MathF.PI - 0.35f;
            var clearlyPort = relative < -0.35f && relative > -MathF.PI + 0.35f;
            if (_side == 0 || (_side < 0 && clearlyStarboard) || (_side > 0 && clearlyPort))
                _side = relative >= 0f ? 1 : -1;

            // Put the target abeam on our chosen side (heading = bearing -/+ 90 degrees), angled in when too far
            // and out when too close.
            var rangeError = Math.Clamp((distance - PreferredRange) / PreferredRange, -1f, 1f);
            var offAbeam = (90f - RangeCorrectionDegrees * rangeError) * MathF.PI / 180f;
            desiredHeading = bearing - _side * offAbeam;
        }

        Steer(world, ship, desiredHeading);

        FireIfBearing(world, ship, target, distance);
    }

    private static void FireIfBearing(World world, Ship ship, Ship target, float distance)
    {
        // Cannonballs carry the firing ship's motion, so what matters is the target's motion relative to us
        // over the shot's flight time.
        var flightSeconds = distance / BroadsideVolley.ProjectileSpeedFor(ship);
        var predicted = target.Position + (target.Velocity - ship.Velocity) * flightSeconds;

        for (var slot = 0; slot < Ship.AbilitySlotCount; slot++)
        {
            var ability = ship.Abilities[slot];
            if (ability is { IsReady: true, Definition: BroadsideVolley volley }
                && volley.Covers(ship, predicted, target.Stats.Radius * AimTightness)
                && !Navigation.LineBlockedByLand(world, ship.Position, predicted))
            {
                world.TryCastAbility(ship, (AbilitySlot)slot, target.Position);
            }
        }
    }

    private static Ship? FindNearestEnemy(World world, Ship ship, float range)
    {
        Ship? nearest = null;
        var nearestDistance = range * range;
        foreach (var other in world.Ships)
        {
            if (other.Team == ship.Team || other.IsSunk)
                continue;
            var d = Vector2.DistanceSquared(other.Position, ship.Position);
            if (d <= nearestDistance)
            {
                nearest = other;
                nearestDistance = d;
            }
        }
        return nearest;
    }
}
