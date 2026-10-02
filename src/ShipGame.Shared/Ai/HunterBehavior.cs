using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Ai;

/// <summary>
/// Aggressive pirate: hunts the nearest enemy ship anywhere on the map and fights it to the death. Chases at full
/// sail; once in range, steers to hold the target abeam at a comfortable distance and fires whichever broadside
/// bears. Steers by rudder, like a player on WASD.
/// </summary>
public sealed class HunterBehavior : INpcBehavior
{
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

    public void Update(World world, Ship ship)
    {
        var target = FindNearestEnemy(world, ship);
        if (target is null)
        {
            // Nothing left to hunt: heave to.
            ship.Rudder = 0;
            ship.MoveTarget = null;
            ship.Throttle = 0;
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

        var headingError = Angles.Delta(ship.Heading, desiredHeading);
        ship.Rudder = MathF.Abs(headingError) < HeadingDeadband ? 0 : MathF.Sign(headingError);

        FireIfBearing(world, ship, target, distance);
    }

    private static void FireIfBearing(World world, Ship ship, Ship target, float distance)
    {
        // Cannonballs carry the firing ship's motion, so what matters is the target's motion relative to us
        // over the shot's flight time.
        var flightSeconds = distance / BroadsideVolley.ProjectileSpeed;
        var predicted = target.Position + (target.Velocity - ship.Velocity) * flightSeconds;

        for (var slot = 0; slot < Ship.AbilitySlotCount; slot++)
        {
            var ability = ship.Abilities[slot];
            if (ability is { IsReady: true, Definition: BroadsideVolley volley }
                && volley.Covers(ship, predicted, target.Stats.Radius * AimTightness))
            {
                world.TryCastAbility(ship, (AbilitySlot)slot, target.Position);
            }
        }
    }

    private static Ship? FindNearestEnemy(World world, Ship ship)
    {
        Ship? nearest = null;
        var nearestDistance = float.MaxValue;
        foreach (var other in world.Ships)
        {
            if (other.Team == ship.Team || other.IsSunk)
                continue;
            var d = Vector2.DistanceSquared(other.Position, ship.Position);
            if (d < nearestDistance)
            {
                nearest = other;
                nearestDistance = d;
            }
        }
        return nearest;
    }
}
