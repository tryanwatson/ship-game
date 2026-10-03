using System.Numerics;

namespace ShipGame.Shared.Simulation;

/// <summary>
/// Arcade ship handling built on one rule: ships turn along arcs. Heading can change by at most
/// (distance travelled / turning radius), so a ship needs way on to steer, and the radius tightens as it slows.
/// Ships always move bow-first. The one exception is rowing: with the sails furled, the helm swings the ship round
/// slowly on the spot (<see cref="RowingTurnRate"/>), so a stopped ship can line up a broadside.
///
/// Steering relies on one geometric fact: turning hard toward a target reaches it if and only if the target lies
/// outside the turning circle, and since that circle stays put while we sail round it, a target outside it stays
/// outside. So the helm only lets the circle grow (speeds up) when the bigger circle can still make the turn, and
/// when the target is inside the circle it holds course until the target drops astern and out of it.
/// </summary>
public static class ShipMovement
{
    public const float ArriveRadius = 0.25f;

    public const int ThrottleLevels = 5;

    /// <summary>Sail setting a stopped ship raises to when given a move order.</summary>
    public const int AutopilotThrottle = 3;

    /// <summary>How fast the crew can row a ship round with the sails furled, in radians per second (20 degrees).</summary>
    public const float RowingTurnRate = 20f * MathF.PI / 180f;

    /// <summary>Rowing: sails furled, helm over, and no move order (move orders always set sail).</summary>
    public static bool IsRowing(Ship ship) => !ship.IsAnchored && ship.Throttle == 0 && ship.MoveTarget is null && ship.Rudder != 0;

    // Fraction of cruise speed used for turns too tight to make at cruise. Lower speed means a tighter circle;
    // players who want tighter still can shorten sail.
    private const float ManeuveringSpeedFactor = 0.6f;

    // A ship making at least this much way holds its course against the wind; below it, the wind's set fades in.
    private const float WindDriftCutoffSpeed = 1f;

    // Seconds for a ship's drift to catch up with the wind, so the set builds and fades rather than snapping.
    private const float WindDriftResponseTime = 1.5f;

    // Room to spare (required radius / turning radius) demanded before ending a hold and committing to a turn.
    // Without it, a target exactly on the edge of the turning circle flips the helm every tick and the ship orbits.
    private const float TurnSlack = 1.2f;

    public static void Step(Ship ship, float dt, Vector2 wind)
    {
        if (ship.IsAnchored)
        {
            // Held fast: no way, no turning, no drift.
            ship.Speed = 0f;
            ship.WindDrift = Vector2.Zero;
            return;
        }

        var stats = ship.Stats;
        var desiredSpeed = 0f;
        var headingError = 0f;
        var canTurn = false;

        if (ship.MoveTarget is { } target)
        {
            var toTarget = target - ship.Position;
            var distance = toTarget.Length();

            if (distance <= ArriveRadius)
            {
                // Arrived: furl sails so the ship comes to rest here rather than sailing on.
                ship.MoveTarget = null;
                ship.IsHoldingCourse = false;
                ship.Throttle = 0;
            }
            else
            {
                headingError = Angles.Delta(ship.Heading, MathF.Atan2(toTarget.Y, toTarget.X));

                // Full cruise when the cruise-speed circle can make the turn (with room to spare), maneuvering speed
                // when it can't. Speeding up therefore never grows the circle past the target. Near the target, ease
                // off along the drift curve: never faster than the speed from which drift alone stops us there. (An
                // all-or-nothing "furl once drift would reach" rule chattered every tick when the target kept moving,
                // e.g. holding right-click with the camera following the ship.)
                var requiredRadius = RequiredTurnRadius(ship, toTarget);
                var cruiseFits = requiredRadius >= stats.TurnRadiusAt(ship.CruiseSpeed) * TurnSlack;
                desiredSpeed = MathF.Min(
                    ship.CruiseSpeed * (cruiseFits ? 1f : ManeuveringSpeedFactor),
                    stats.SpeedForStoppingDistance(distance));

                // Judge the turn by the widest circle we'll sail this tick, so a ship still gathering way doesn't
                // commit to a turn it's about to outgrow.
                var turnRadius = stats.TurnRadiusAt(MathF.Max(ship.Speed, desiredSpeed));

                if (ship.IsHoldingCourse)
                {
                    if (requiredRadius >= turnRadius * TurnSlack)
                        ship.IsHoldingCourse = false;
                }
                else if (requiredRadius < turnRadius)
                {
                    ship.IsHoldingCourse = true;
                }

                canTurn = !ship.IsHoldingCourse;
            }
        }
        else
        {
            // Manual sailing: hold cruise speed and steer by the rudder at full lock.
            desiredSpeed = ship.CruiseSpeed;
            headingError = ship.Rudder * MathF.PI;
            canTurn = ship.Rudder != 0;
        }

        ship.Speed = desiredSpeed > ship.Speed
            ? MathF.Min(desiredSpeed, ship.Speed + stats.Acceleration * dt)
            : MathF.Max(desiredSpeed, ship.Speed - stats.DecelerationAt(ship.Speed) * dt);

        // Heading changes by at most arc length / radius: no way on, no turning. Unless rowing, which turns at least
        // at the rowing rate (a ship still coasting after furling keeps its faster arc while that lasts).
        var maxTurn = ship.Speed > 0f ? ship.Speed * dt / stats.TurnRadiusAt(ship.Speed) : 0f;
        if (IsRowing(ship))
            maxTurn = MathF.Max(maxTurn, RowingTurnRate * dt);
        if (canTurn && maxTurn > 0f)
            ship.Heading = Angles.Wrap(ship.Heading + Math.Clamp(headingError, -maxTurn, maxTurn));

        // A ship that isn't making way is set downwind; under way, the sails and keel hold her course.
        var exposure = 1f - MathF.Min(1f, ship.Speed / WindDriftCutoffSpeed);
        var targetDrift = wind * exposure;
        ship.WindDrift += (targetDrift - ship.WindDrift) * MathF.Min(1f, dt / WindDriftResponseTime);

        ship.Position += (ship.Forward * ship.Speed + ship.WindDrift) * dt;
    }

    /// <summary>
    /// Radius of the arc that leaves the ship on its current heading and passes through the target:
    /// d^2 / (2 * lateral offset). Any turn at this radius or tighter can reach it; wider turns can't
    /// (the target sits inside their turning circle). Infinite for targets dead ahead or astern.
    /// </summary>
    public static float RequiredTurnRadius(Ship ship, Vector2 toTarget)
    {
        var forward = ship.Forward;
        var lateral = MathF.Abs(Vector2.Dot(toTarget, new Vector2(-forward.Y, forward.X)));
        return lateral < 1e-5f ? float.PositiveInfinity : toTarget.LengthSquared() / (2f * lateral);
    }

    /// <summary>Pushes overlapping ships apart. Circle colliders are a placeholder until hulls get proper shapes.</summary>
    public static void ResolveCollisions(IReadOnlyList<Ship> ships)
    {
        for (var i = 0; i < ships.Count; i++)
        {
            for (var j = i + 1; j < ships.Count; j++)
            {
                var a = ships[i];
                var b = ships[j];
                var offset = b.Position - a.Position;
                var distance = offset.Length();
                var minDistance = a.Stats.Radius + b.Stats.Radius;
                if (distance >= minDistance || distance < 1e-5f)
                    continue;

                var push = offset / distance * ((minDistance - distance) / 2f);
                a.Position -= push;
                b.Position += push;
            }
        }
    }

    public static void ClampToBounds(Ship ship, Vector2 min, Vector2 max)
    {
        var r = new Vector2(ship.Stats.Radius);
        ship.Position = Vector2.Clamp(ship.Position, min + r, max - r);
    }
}
