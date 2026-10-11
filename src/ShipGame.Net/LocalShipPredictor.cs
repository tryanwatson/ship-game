using System.Numerics;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Simulation;

namespace ShipGame.Net;

/// <summary>
/// Client-side prediction for the player's own ship, so it answers the helm at once instead of a round trip later.
/// Every frame it takes the ship as of the newest snapshot, then re-runs the real simulation (a one-ship
/// <see cref="World"/> with the region's islands) forward to the prediction tick, replaying the movement commands the
/// server hasn't confirmed yet at the ticks they're expected to land. Using the whole world step rather than just
/// <see cref="ShipMovement.Step"/> gets anchoring, groundings and command rules exactly as the server applies them.
///
/// When a snapshot disagrees with what was predicted (a command landed a tick late, another ship shoved us), the
/// difference is drawn as an offset that fades over <see cref="CorrectionTimeConstant"/> rather than a jump.
/// Other ships aren't in the replay, so collisions with them are only ever corrected, never predicted.
/// </summary>
public sealed class LocalShipPredictor
{
    /// <summary>Seconds for a misprediction's visual offset to fade to about a third.</summary>
    public const float CorrectionTimeConstant = 0.1f;

    /// <summary>A correction bigger than this (tiles) is a teleport, such as a respawn: snap, don't glide.</summary>
    public const float SnapDistance = 3f;

    /// <summary>Never replay more than this many ticks (3 s), whatever the clock says.</summary>
    public const int MaxReplayTicks = 90;

    // Unacknowledged commands this many ticks older than the newest snapshot are given up on (the server dropped
    // them, or never applied them); otherwise they'd be replayed forever.
    private const int StaleCommandTicks = SimConstants.TickRate;

    private readonly record struct PendingCommand(uint Sequence, long ApplyTick, Command Command);

    private readonly List<PendingCommand> _pending = new();
    private readonly World _scratch;
    private Ship? _ship;
    private int _shipId = -1;
    private int _statsVersion = -1;
    private long _baseTick = -1;
    private long _lastApplyTick;

    // Last frame's raw prediction and its rates, to judge how far a new snapshot moved it.
    private bool _hasLast;
    private Vector2 _lastPosition;
    private float _lastHeading;
    private Vector2 _velocity;
    private float _turnRate;

    private Vector2 _positionError;
    private float _headingError;

    /// <param name="islands">The region's islands, which the ship can run aground on.</param>
    public LocalShipPredictor(Vector2 worldSize, IEnumerable<Island>? islands = null)
    {
        _scratch = new World(worldSize);
        foreach (var island in islands ?? Array.Empty<Island>())
            _scratch.AddIsland(island);
    }

    /// <summary>
    /// A predictor for a new region (<paramref name="worldSize"/>, <paramref name="islands"/>) that still owes the
    /// server's word on the commands this one was waiting on, so none sent across the change are lost.
    /// </summary>
    public LocalShipPredictor ForRegion(Vector2 worldSize, IEnumerable<Island> islands)
    {
        var next = new LocalShipPredictor(worldSize, islands) { _lastApplyTick = _lastApplyTick };
        next._pending.AddRange(_pending);
        return next;
    }

    /// <summary>Where to draw the ship (prediction plus any fading correction).</summary>
    public Vector2 Position { get; private set; }

    public float Heading { get; private set; }

    /// <summary>The predicted ship's movement state as of the prediction tick (throttle, anchor, move target...).</summary>
    public Ship? Predicted => _ship;

    /// <summary>Commands sent but not yet confirmed by a snapshot.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Only commands that change how the ship moves are worth replaying.</summary>
    public static bool AffectsMovement(Command command) =>
        command is MoveCommand or StopCommand or AdjustThrottleCommand or SetRudderCommand or AnchorKeyCommand;

    /// <summary>
    /// A command just sent with this sequence number, expected to be applied in the server step that produces
    /// <paramref name="applyTick"/>.
    /// </summary>
    public void Record(uint sequence, Command command, long applyTick)
    {
        if (!AffectsMovement(command))
            return;
        // Keep apply ticks in send order even if the prediction clock steps back, so the replay keeps their order.
        _lastApplyTick = Math.Max(_lastApplyTick, applyTick);
        _pending.Add(new PendingCommand(sequence, _lastApplyTick, command));
    }

    /// <summary>
    /// Re-predicts the ship from the newest snapshot. <paramref name="replicaShip"/> supplies the hull, guns and
    /// upgrades; <paramref name="state"/> and <paramref name="baseTick"/> the server's word on where it was;
    /// <paramref name="acked"/> the last command the server had applied by then.
    /// </summary>
    public void Update(Ship replicaShip, ShipState state, long baseTick, uint acked, Vector2 wind, double predictTick, double elapsedSeconds)
    {
        _pending.RemoveAll(p => p.Sequence <= acked || p.ApplyTick < baseTick - StaleCommandTicks);
        var ship = EnsureShip(replicaShip);
        var baseChanged = baseTick != _baseTick;
        _baseTick = baseTick;

        var target = Math.Clamp(predictTick, baseTick, baseTick + MaxReplayTicks - 1);
        var endTick = (long)Math.Floor(target);
        var fraction = (float)(target - endTick);

        Load(ship, state);
        _scratch.SetTick(baseTick);
        _scratch.Wind = wind;

        // Replay to endTick (A) and one more (B), then blend: like the local session's interpolation alpha.
        var positionA = ship.Position;
        var headingA = ship.Heading;
        var next = 0;
        for (var tick = baseTick + 1; tick <= endTick + 1; tick++)
        {
            while (next < _pending.Count && _pending[next].ApplyTick <= tick)
                _scratch.Enqueue(_pending[next++].Command);
            _scratch.Step();
            if (tick == endTick)
            {
                positionA = ship.Position;
                headingA = ship.Heading;
            }
        }
        _scratch.DrainEvents();

        var raw = Vector2.Lerp(positionA, ship.Position, fraction);
        var rawHeading = Angles.Lerp(headingA, ship.Heading, fraction);

        var dt = (float)elapsedSeconds;
        if (_hasLast && baseChanged)
        {
            // Where last frame's prediction was heading, versus where the new snapshot puts us: keep drawing the
            // former and let the gap fade.
            _positionError += _lastPosition + _velocity * dt - raw;
            _headingError += Angles.Delta(rawHeading, _lastHeading + _turnRate * dt);
            if (_positionError.Length() > SnapDistance)
            {
                _positionError = Vector2.Zero;
                _headingError = 0f;
            }
        }
        var decay = MathF.Exp(-dt / CorrectionTimeConstant);
        _positionError *= decay;
        _headingError *= decay;

        _velocity = (ship.Position - positionA) * SimConstants.TickRate;
        _turnRate = Angles.Delta(headingA, ship.Heading) * SimConstants.TickRate;
        _lastPosition = raw;
        _lastHeading = rawHeading;
        _hasLast = true;

        Position = raw + _positionError;
        Heading = Angles.Wrap(rawHeading + _headingError);
    }

    /// <summary>The ship is gone (sunk, or the run ended): forget it, so a new one starts fresh.</summary>
    public void Forget()
    {
        if (_ship is not null)
            _scratch.RemoveShip(_ship.Id);
        _ship = null;
        _shipId = -1;
        _hasLast = false;
        _positionError = Vector2.Zero;
        _headingError = 0f;
    }

    /// <summary>The scratch copy of the ship, rebuilt when it's a new ship or its hull or upgrades changed.</summary>
    private Ship EnsureShip(Ship replicaShip)
    {
        if (_ship is not null && _shipId == replicaShip.Id && _statsVersion == replicaShip.StatsVersion)
            return _ship;

        var sameShip = _shipId == replicaShip.Id;
        if (_ship is not null)
            _scratch.RemoveShip(_ship.Id);
        var abilities = replicaShip.Abilities.Select(a => a?.Definition).ToList();
        _ship = _scratch.SpawnShip(replicaShip.Position, replicaShip.Heading, replicaShip.BaseStats, replicaShip.OwnerPlayerId, abilities, replicaShip.Id);
        _ship.Team = replicaShip.Team;
        _ship.ReplaceModifiers(replicaShip.Modifiers);
        _shipId = replicaShip.Id;
        _statsVersion = replicaShip.StatsVersion;
        if (!sameShip)
        {
            _hasLast = false;
            _positionError = Vector2.Zero;
            _headingError = 0f;
        }
        return _ship;
    }

    private static void Load(Ship ship, ShipState state)
    {
        ship.Position = ship.PreviousPosition = state.Position;
        ship.Heading = ship.PreviousHeading = state.Heading;
        ship.Speed = state.Speed;
        ship.Health = Math.Max(state.Health, 1f); // the replay is about movement; never let it sink the copy
        ship.Throttle = state.Throttle;
        ship.Rudder = state.Rudder;
        ship.Anchor = state.Anchor;
        ship.AnchorRaiseTicksRemaining = state.AnchorRaiseTicks;
        ship.AnchorDropTicksRemaining = state.AnchorDropTicks;
        ship.MoveTarget = state.MoveTarget;
        ship.IsHoldingCourse = state.IsHoldingCourse;
        ship.WindDrift = state.WindDrift;
        ship.PlunderIslandId = null; // plundering pays out on the server; don't let the copy start one
        ship.PlunderTicks = 0;
    }
}
