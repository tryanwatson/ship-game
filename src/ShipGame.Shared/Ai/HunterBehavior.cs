using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Ai;

public enum HunterState
{
    /// <summary>Going about its <see cref="HunterBehavior.Orders"/>, watching for enemies.</summary>
    Patrolling,

    /// <summary>Chasing and fighting a target.</summary>
    Hunting,

    /// <summary>Leashed: sailing back to its patch, ignoring enemies until it gets there.</summary>
    Returning,
}

/// <summary>
/// Aggressive pirate. Between fights it follows its <see cref="Orders"/>: cruising round the patch it guards, or
/// roaming its sea, or keeping station on the leader of its <see cref="Group"/>. It goes for enemies that come within
/// <see cref="AggroRange"/> of it, trespassers on the patch it guards, whoever shoots it, and whoever the rest of its
/// group is fighting; it chases at full sail and, once in range, steers to hold the target abeam at a comfortable
/// distance and fires whichever broadside bears. Steers by rudder, like a player on WASD. Leashed: if dragged too far
/// from its patch (or, roaming, from where the chase began), or the target gets away, it sails back and carries on.
///
/// A <see cref="Relentless"/> hunter (a raider) has no orders: it hunts from the moment it spawns, always going
/// after the nearest enemy wherever it is, and never gives up the chase.
/// </summary>
public sealed class HunterBehavior : INpcBehavior
{
    /// <summary>Enemies within this many tiles of a patrolling pirate draw it out.</summary>
    public const float AggroRange = 20f;

    /// <summary>A hunting pirate gives up once its target is this far away. Above <see cref="AggroRange"/> so a
    /// player loitering at the aggro edge doesn't toggle it on and off.</summary>
    public const float DisengageRange = 30f;

    /// <summary>A hunting pirate turns back once it's this far from its patch (beyond its watch), or from where a
    /// roaming chase began.</summary>
    public const float LeashRange = 40f;

    /// <summary>
    /// A pirate that's hit goes for whoever hit it, however far off (guns like the mortar outrange its sight), and
    /// for this long after each hit doesn't give up on them for distance. Its leash still holds.
    /// </summary>
    public const float ProvokedSeconds = 8f;
    public static readonly int ProvokedTicks = (int)(ProvokedSeconds * SimConstants.TickRate);

    /// <summary>
    /// On its way back, a pirate only turns on an attacker while it's this far inside its leash, so one shelled
    /// at the end of its tether doesn't flip back and forth.
    /// </summary>
    private const float ReprovokeLeashMargin = 10f;

    /// <summary>A pirate only answers its group's call to arms from this close to the fight.</summary>
    public const float RallyRange = LeashRange;

    private const int ChaseThrottle = ShipMovement.ThrottleLevels;
    private const int EngageThrottle = 4;

    // Cruising between fights: guards amble, rovers make way, and either leaves its followers sail in hand to keep up.
    private const int GuardThrottle = 2;
    private const int RoamThrottle = 3;

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

    // Close enough to a station (radius 0 post) to drop anchor.
    private const float HomeArrivalDistance = 2f;

    // Close enough to a waypoint to pick the next; and how long to try for one before giving it up as unreachable.
    private const float WaypointArrivalDistance = 3f;
    private static readonly int WaypointGiveUpTicks = 40 * SimConstants.TickRate;

    // A guard sometimes lies to for a while on reaching a waypoint.
    private const double GuardPauseChance = 0.4;
    private const float MinPauseSeconds = 2f;
    private const float MaxPauseSeconds = 6f;

    // A guard going round an island steps this far round it (degrees) per waypoint.
    private const float MinGuardStepDegrees = 35f;
    private const float MaxGuardStepDegrees = 110f;

    // Waypoints are open water this far from land and the map's edge.
    private const float WaypointClearance = 4f;
    private const int WaypointAttempts = 24;

    // Rovers set off for somewhere at least this far away, so they cover their sea rather than milling about.
    private const float MinRoamLeg = 30f;

    // A rover that broke off a chase is back on its way once it's this close to where the chase began.
    private const float RoamReturnArrival = 8f;

    // A follower further than this off its station crowds on sail; ahead of it, it eases off.
    private const float StationTolerance = 2f;

    // Which side we're presenting: +1 starboard, -1 port, 0 not yet chosen. Sticky, so the ship doesn't flip
    // sides every time the target crosses the bow.
    private int _side;

    private Ship? _target;

    // The last hit we've reacted to, and until when we're provoked.
    private long? _seenHitTick;
    private long _provokedUntilTick = -1;

    // Which way we last swerved around land; see Navigation.ChooseHeading.
    private int _avoidSide;

    private readonly Random _rng;

    // Where we're cruising to, until when we'll keep trying, and until when we're lying to.
    private Vector2? _waypoint;
    private long _waypointDeadline;
    private long _pausedUntilTick = -1;

    // A guard going round an island keeps going the same way round: +1 or -1.
    private readonly int _roundDirection;

    // Where the current (or last) chase began: a rover's leash runs from here.
    private Vector2 _chaseOrigin;

    /// <param name="relentless">A raider: always chasing the nearest enemy, with no leash and no post to return to.</param>
    /// <param name="prey">Which enemies it will go after (by default, any); others it ignores, even when they're nearest.</param>
    public HunterBehavior(Vector2 home, bool relentless = false, Func<World, Ship, bool>? prey = null)
        : this(new GuardPost(home, 0f), prey: prey)
    {
        Relentless = relentless;
        if (relentless)
            State = HunterState.Hunting;
    }

    /// <param name="seed">Drives where it wanders; the same seed sails the same course.</param>
    /// <param name="group">The pirates it sails with, if any (it should already be a member).</param>
    public HunterBehavior(PirateOrders orders, int seed = 0, PirateGroup? group = null, Func<World, Ship, bool>? prey = null)
    {
        Orders = orders;
        Group = group;
        _prey = prey;
        _rng = new Random(seed);
        _roundDirection = _rng.Next(2) == 0 ? 1 : -1;
        if (orders is GuardPost post)
            _chaseOrigin = post.Center;
    }

    public bool Relentless { get; }

    public PirateOrders Orders { get; }

    public PirateGroup? Group { get; }

    private readonly Func<World, Ship, bool>? _prey;

    /// <summary>Where the pirate returns to when leashed: the center of its post, or where a roaming chase began.</summary>
    public Vector2 Home => Orders is GuardPost post ? post.Center : _chaseOrigin;

    public HunterState State { get; private set; } = HunterState.Patrolling;

    public Ship? Target => _target;

    /// <summary>Where it's cruising to between fights, if anywhere.</summary>
    public Vector2? Waypoint => _waypoint;

    public void Update(World world, Ship ship)
    {
        UpdateState(world, ship);
        ship.Stance = State switch
        {
            HunterState.Patrolling => NpcStance.Patrolling,
            HunterState.Hunting => NpcStance.Hunting,
            _ => NpcStance.Returning,
        };
    }

    private void UpdateState(World world, Ship ship)
    {
        ReactToHits(world, ship);
        switch (State)
        {
            case HunterState.Patrolling:
                Patrol(world, ship);
                break;
            case HunterState.Hunting:
                Hunt(world, ship);
                break;
            case HunterState.Returning:
                SailBack(world, ship);
                break;
        }
    }

    /// <summary>
    /// Damage always draws a pirate's attention: a patrolling one (or one heading back, well inside its leash) goes
    /// straight for whoever hit it, and one already chasing that attacker stays provoked. Raiders are always hunting anyway.
    /// </summary>
    private void ReactToHits(World world, Ship ship)
    {
        if (ship.LastHitTick is not { } hitTick || hitTick == _seenHitTick)
            return;
        _seenHitTick = hitTick;
        if (Relentless || ship.LastHitByShipId is not { } attackerId || world.FindShip(attackerId) is not { } attacker
            || attacker.IsSunk || attacker.Team == ship.Team)
            return;

        switch (State)
        {
            case HunterState.Hunting when _target == attacker:
                _provokedUntilTick = world.Tick + ProvokedTicks;
                break;
            case HunterState.Hunting:
                break; // busy with someone else
            case HunterState.Returning when Vector2.Distance(ship.Position, LeashAnchor) > LeashLimit - ReprovokeLeashMargin:
                break;
            default:
                Engage(ship, attacker);
                _provokedUntilTick = world.Tick + ProvokedTicks;
                break;
        }
    }

    // ---- Between fights ----------------------------------------------------------------------------------------

    private void Patrol(World world, Ship ship)
    {
        if (SpotEnemy(world, ship) is { } enemy)
        {
            Engage(ship, enemy);
            // Called in by the group: it sails to the fight, however far off the target is, like one shot at.
            if (enemy == Group?.Rallying(world))
                _provokedUntilTick = world.Tick + ProvokedTicks;
            Hunt(world, ship); // engage this tick
            return;
        }

        if (Group?.Leader(world) is { } leader && leader != ship)
        {
            KeepStation(world, ship, leader);
            return;
        }

        switch (Orders)
        {
            case GuardPost { Radius: <= 0f } post:
                HoldStation(world, ship, post.Center);
                break;
            default:
                Cruise(world, ship);
                break;
        }
    }

    /// <summary>
    /// Who a patrolling pirate goes for, if anyone: whoever its group is fighting nearby, then the nearest enemy it
    /// can see, then the nearest trespasser on the patch it guards.
    /// </summary>
    private Ship? SpotEnemy(World world, Ship ship)
    {
        if (Group?.Rallying(world) is { } rally && rally.Team != ship.Team && IsPrey(world, rally)
            && Vector2.Distance(rally.Position, ship.Position) <= RallyRange)
            return rally;
        if (FindNearestEnemy(world, ship, ship.Position, AggroRange) is { } seen)
            return seen;
        return Orders is GuardPost { Watch: > 0f } post ? FindNearestEnemy(world, ship, post.Center, post.Watch) : null;
    }

    private void Engage(Ship ship, Ship target)
    {
        _target = target;
        _side = 0;
        _waypoint = null;
        _pausedUntilTick = -1;
        ship.IsAnchored = false;
        if (State == HunterState.Patrolling)
            _chaseOrigin = ship.Position;
        State = HunterState.Hunting;
    }

    /// <summary>Sails to <paramref name="center"/> and rides at anchor there.</summary>
    private void HoldStation(World world, Ship ship, Vector2 center)
    {
        var toCenter = center - ship.Position;
        var distance = toCenter.Length();
        if (distance <= HomeArrivalDistance)
        {
            ship.Rudder = 0;
            ship.Throttle = 0;
            ship.IsAnchored = true;
            return;
        }

        ship.IsAnchored = false;
        ship.MoveTarget = null;
        ship.Throttle = ship.Stats.StoppingDistance(ship.Speed) >= distance - HomeArrivalDistance / 2f ? 0 : ChaseThrottle;
        Steer(world, ship, MathF.Atan2(toCenter.Y, toCenter.X));
    }

    /// <summary>Under easy sail from one waypoint to the next, lying to for a spell at some of them.</summary>
    private void Cruise(World world, Ship ship)
    {
        ship.IsAnchored = false;
        ship.MoveTarget = null;
        if (world.Tick < _pausedUntilTick)
        {
            ship.Throttle = 0;
            ship.Rudder = 0;
            return;
        }

        if (_waypoint is not { } waypoint || world.Tick >= _waypointDeadline
            || Vector2.Distance(ship.Position, waypoint) <= WaypointArrivalDistance)
        {
            var arrived = _waypoint is not null;
            waypoint = PickWaypoint(world, ship);
            _waypoint = waypoint;
            _waypointDeadline = world.Tick + WaypointGiveUpTicks;
            if (arrived && Orders is GuardPost && _rng.NextDouble() < GuardPauseChance)
            {
                var seconds = MinPauseSeconds + (float)_rng.NextDouble() * (MaxPauseSeconds - MinPauseSeconds);
                _pausedUntilTick = world.Tick + (int)(seconds * SimConstants.TickRate);
                ship.Throttle = 0;
                ship.Rudder = 0;
                return;
            }
        }

        var toWaypoint = waypoint - ship.Position;
        ship.Throttle = Orders is RoamOrders ? RoamThrottle : GuardThrottle;
        Steer(world, ship, MathF.Atan2(toWaypoint.Y, toWaypoint.X));
    }

    /// <summary>
    /// Holds its place in the leader's V: steers for a point a little ahead of its station, and sets more sail than
    /// the leader while astern of it, less while ahead.
    /// </summary>
    private void KeepStation(World world, Ship ship, Ship leader)
    {
        ship.IsAnchored = false;
        ship.MoveTarget = null;
        var station = Group!.StationFor(world, leader, ship);
        var forward = new Vector2(MathF.Cos(leader.Heading), MathF.Sin(leader.Heading));
        var offset = station - ship.Position;
        var behind = Vector2.Dot(offset, forward); // + we're astern of our station

        if (leader.Throttle == 0 && offset.Length() <= StationTolerance * 2f)
        {
            ship.Throttle = 0;
            ship.Rudder = 0;
            return;
        }

        var correction = behind > StationTolerance ? (behind > 4f * StationTolerance ? 2 : 1) : behind < -StationTolerance ? -1 : 0;
        ship.Throttle = Math.Clamp(Math.Max(leader.Throttle, 1) + correction, 0, ShipMovement.ThrottleLevels);
        var aim = station + forward * 6f;
        Steer(world, ship, MathF.Atan2(aim.Y - ship.Position.Y, aim.X - ship.Position.X));
    }

    /// <summary>Somewhere to cruise to next under its orders: open water, inside the map.</summary>
    private Vector2 PickWaypoint(World world, Ship ship)
    {
        var best = ship.Position;
        var bestClearance = float.MinValue;
        for (var attempt = 0; attempt < WaypointAttempts; attempt++)
        {
            var candidate = Orders switch
            {
                GuardPost post => GuardWaypoint(ship, post, relaxed: attempt >= WaypointAttempts / 2),
                RoamOrders roam => RoamWaypoint(world, roam),
                _ => ship.Position,
            };
            candidate = Vector2.Clamp(candidate, new Vector2(WaypointClearance), world.WorldSize - new Vector2(WaypointClearance));
            if (Orders is RoamOrders && attempt < WaypointAttempts / 2 && Vector2.Distance(candidate, ship.Position) < MinRoamLeg)
                continue;
            var clearance = world.DistanceToLand(candidate);
            if (clearance >= WaypointClearance)
                return candidate;
            if (clearance > bestClearance)
            {
                best = candidate;
                bestClearance = clearance;
            }
        }
        return best;
    }

    /// <summary>
    /// A spot on the patch: round an island, the next step on round it the way this pirate goes; on open water,
    /// anywhere on the patch.
    /// </summary>
    private Vector2 GuardWaypoint(Ship ship, GuardPost post, bool relaxed)
    {
        float angle;
        var fromCenter = ship.Position - post.Center;
        if (post.MinRadius > 0f && !relaxed)
        {
            var step = MinGuardStepDegrees + (float)_rng.NextDouble() * (MaxGuardStepDegrees - MinGuardStepDegrees);
            angle = MathF.Atan2(fromCenter.Y, fromCenter.X) + _roundDirection * step * MathF.PI / 180f;
        }
        else
        {
            angle = (float)_rng.NextDouble() * MathF.Tau;
        }
        var radius = post.MinRadius + (float)_rng.NextDouble() * (post.Radius - post.MinRadius);
        return post.Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    private Vector2 RoamWaypoint(World world, RoamOrders roam)
    {
        var x = WaypointClearance + (float)_rng.NextDouble() * (world.WorldSize.X - 2f * WaypointClearance);
        var y = roam.North + WaypointClearance + (float)_rng.NextDouble() * MathF.Max(0f, roam.South - roam.North - 2f * WaypointClearance);
        return new Vector2(x, y);
    }

    // ---- Leash -------------------------------------------------------------------------------------------------

    /// <summary>What its leash is tied to: its post, or, roaming, where the chase began.</summary>
    private Vector2 LeashAnchor => Orders is GuardPost post ? post.Center : _chaseOrigin;

    /// <summary>How far from <see cref="LeashAnchor"/> it will chase: further for a guard watching a wide patch.</summary>
    private float LeashLimit => Orders is GuardPost post ? MathF.Max(LeashRange, post.Watch + AggroRange) : LeashRange;

    private void StartReturning(Ship ship)
    {
        State = HunterState.Returning;
        _target = null;
        ship.MoveTarget = null;
        ship.Throttle = ChaseThrottle;
    }

    private void StartPatrolling(Ship ship)
    {
        State = HunterState.Patrolling;
        _target = null;
        _waypoint = null;
        ship.MoveTarget = null;
    }

    /// <summary>
    /// Steers back by hand rather than autopilot, so the trip goes round islands, and takes up its patrol once there.
    /// A station-keeper takes in sail once the drift will carry it the rest of the way, and anchors on arrival.
    /// </summary>
    private void SailBack(World world, Ship ship)
    {
        var toHome = Home - ship.Position;
        var distance = toHome.Length();
        switch (Orders)
        {
            case GuardPost { Radius: <= 0f }:
                if (distance <= HomeArrivalDistance)
                {
                    StartPatrolling(ship);
                    HoldStation(world, ship, Home);
                    return;
                }
                ship.Throttle = ship.Stats.StoppingDistance(ship.Speed) >= distance - HomeArrivalDistance / 2f ? 0 : ChaseThrottle;
                break;
            case GuardPost post when distance <= post.Radius:
            case RoamOrders when distance <= RoamReturnArrival:
                StartPatrolling(ship);
                Patrol(world, ship);
                return;
            default:
                ship.Throttle = ChaseThrottle;
                break;
        }
        Steer(world, ship, MathF.Atan2(toHome.Y, toHome.X));
    }

    /// <summary>Puts the helm over toward <paramref name="desiredHeading"/>, or the nearest heading clear of land.</summary>
    private void Steer(World world, Ship ship, float desiredHeading)
    {
        var heading = Navigation.ChooseHeading(world, ship, desiredHeading, ref _avoidSide);
        var headingError = Angles.Delta(ship.Heading, heading);
        ship.Rudder = MathF.Abs(headingError) < HeadingDeadband ? 0 : MathF.Sign(headingError);
    }

    // ---- Fighting ----------------------------------------------------------------------------------------------

    private void Hunt(World world, Ship ship)
    {
        // A raider goes for whoever is nearest right now, however far; with nobody afloat it waits, sails furled.
        if (Relentless)
        {
            _target = FindNearestEnemy(world, ship, ship.Position, float.PositiveInfinity);
            if (_target is null)
            {
                ship.Throttle = 0;
                ship.Rudder = 0;
                return;
            }
        }

        // Lost the target (sunk): take on another one in sight, or go back.
        if (_target is null || _target.IsSunk)
        {
            _target = SpotEnemy(world, ship);
            if (_target is null)
            {
                StartReturning(ship);
                return;
            }
        }

        var target = _target;
        if (!Relentless
            && (Vector2.Distance(ship.Position, LeashAnchor) > LeashLimit
                || (world.Tick >= _provokedUntilTick && Vector2.Distance(ship.Position, target.Position) > DisengageRange
                    && !Trespassing(target))))
        {
            StartReturning(ship);
            return;
        }

        Group?.Report(target, world.Tick);

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

    /// <summary>Whether <paramref name="target"/> is on the patch this pirate guards.</summary>
    private bool Trespassing(Ship target) =>
        Orders is GuardPost { Watch: > 0f } post && Vector2.Distance(target.Position, post.Center) <= post.Watch;

    private static void FireIfBearing(World world, Ship ship, Ship target, float distance)
    {
        // Cannonballs carry the firing ship's motion, so what matters is the target's motion relative to us
        // over the shot's flight time.
        var flightSeconds = distance / BroadsideVolley.ProjectileSpeedFor(ship);
        var predicted = target.Position + (target.Velocity - ship.Velocity) * flightSeconds;

        for (var slot = 0; slot < Ship.AbilitySlotCount; slot++)
        {
            var ability = ship.Abilities[slot];
            if (ability is not { Definition: BroadsideVolley })
                continue;

            // Aim at the predicted position: the broadside fires whichever side that's on, if that side is loaded.
            var side = BroadsideVolley.SideCovering(ship, predicted, target.Stats.Radius * AimTightness);
            if (side != BroadsideSide.None
                && ability.IsChannelReady(BroadsideVolley.ChannelOf(side))
                && !Navigation.LineBlockedByLand(world, ship.Position, predicted))
                world.TryCastAbility(ship, (AbilitySlot)slot, predicted);
        }
    }

    private bool IsPrey(World world, Ship other) => _prey is null || _prey(world, other);

    /// <summary>The nearest enemy it would go after within <paramref name="range"/> of <paramref name="from"/>.</summary>
    private Ship? FindNearestEnemy(World world, Ship ship, Vector2 from, float range)
    {
        Ship? nearest = null;
        var nearestDistance = range * range;
        foreach (var other in world.Ships)
        {
            if (other.Team == ship.Team || other.IsSunk || !IsPrey(world, other))
                continue;
            var d = Vector2.DistanceSquared(other.Position, from);
            if (d <= nearestDistance)
            {
                nearest = other;
                nearestDistance = d;
            }
        }
        return nearest;
    }
}
