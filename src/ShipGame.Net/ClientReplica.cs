using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Trading;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Net;

/// <summary>
/// The client's copy of the server's world, rebuilt from what arrives over the wire. It never runs the game
/// rules; it only mirrors them, drawn <see cref="InterpolationDelayTicks"/> behind the newest snapshot so there's
/// always a snapshot either side of "now" to blend between. Events are held back until that same render clock
/// reaches them, so a volley appears exactly when the drawn ship fires it. Cannonballs are flown locally from
/// their spawn event (they travel in straight lines) and removed when the server reports an impact.
///
/// The exception is the player's own ship, which is predicted (see <see cref="LocalShipPredictor"/>): drawn where
/// it will be once the commands sent now reach the server, a round trip ahead of the newest snapshot, so the helm
/// answers at once. Everything else, including the ship's guns and health, stays on the server's timeline.
/// </summary>
public sealed class ClientReplica
{
    /// <summary>How far behind the newest snapshot to draw: 1.5 snapshot intervals, so one lost packet doesn't stall motion.</summary>
    public const double InterpolationDelayTicks = Protocol.SnapshotEveryTicks * 1.5;

    // If the local clock drifts this far from the server's, jump rather than glide back.
    private const double ClockSnapTicks = 8;

    // Predict this much further than the round trip alone, since a command waits up to a tick on the server.
    private const double PredictionMarginTicks = 1;

    /// <summary>
    /// The predicted ship is drawn ahead of where the server fired from, so its own shots would appear behind it.
    /// They start at the drawn hull instead and blend onto their true path over this many ticks, well before most
    /// hits (which, like the targets, stay on the server's timeline).
    /// </summary>
    public const double ShotConvergeTicks = 12;

    private readonly List<Snapshot> _snapshots = new();
    private readonly List<WorldEvent> _pendingEvents = new();
    private readonly List<ShipInfo> _pendingShipInfos = new();
    private readonly List<WorldEvent> _appliedEvents = new();
    private readonly Dictionary<int, ProjectileSpawned> _projectiles = new();
    private readonly Dictionary<int, Vector2> _shotOffsets = new();

    // Last frame's local ship: where the server's timeline had it, and where it was drawn (predicted).
    private bool _hasOwnShipFrames;
    private Vector2 _ownTimelinePosition;
    private float _ownTimelineHeading;
    private Vector2 _ownDrawnPosition;
    private float _ownDrawnHeading;
    private double _clock;
    private bool _clockStarted;
    private double _predictClock;
    private LocalShipPredictor _predictor = new(Archipelago.Size);

    public ClientReplica()
    {
        World = CreateWorld(Archipelago.Size, Vector2.Zero);
    }

    /// <summary>Whose ship to predict; 0 for none.</summary>
    public int LocalPlayerId { get; set; }

    /// <summary>Predict the local player's ship (on by default).</summary>
    public bool PredictLocalShip { get; set; } = true;

    /// <summary>Round trip to the server, which sets how far ahead to predict. Kept up to date by the connection.</summary>
    public double RoundTripSeconds { get; set; }

    /// <summary>The tick the local ship is drawn at, and the one a command sent now is expected to land on.</summary>
    public double PredictTick => _predictClock;

    /// <summary>For tests and diagnostics.</summary>
    public LocalShipPredictor Predictor => _predictor;

    /// <summary>The mirrored world: draw this.</summary>
    public World World { get; private set; }

    /// <summary>Blend factor between each ship's PreviousPosition (older snapshot) and Position (newer).</summary>
    public float Alpha { get; private set; } = 1f;

    /// <summary>The server tick currently being drawn (fractional).</summary>
    public double RenderTick { get; private set; }

    public long LatestSnapshotTick => _snapshots.Count == 0 ? -1 : _snapshots[^1].Tick;

    /// <summary>A new run: start from an empty world on the shared map.</summary>
    public void Reset(RunStart start)
    {
        World = CreateWorld(start.WorldSize, start.Wind);
        World.FriendlyFire = start.FriendlyFire;
        World.SetTick(start.Tick);
        _snapshots.Clear();
        _pendingEvents.Clear();
        _pendingShipInfos.Clear();
        _projectiles.Clear();
        _shotOffsets.Clear();
        _hasOwnShipFrames = false;
        _clockStarted = false;
        _predictor = new LocalShipPredictor(start.WorldSize);
        Alpha = 1f;
    }

    /// <summary>
    /// A command was just sent with this sequence number: replay it in the prediction until the server confirms it.
    /// </summary>
    public void OnCommandSent(Command command, uint sequence) =>
        // As on the server, it's ours whatever player it names.
        _predictor.Record(sequence, command with { PlayerId = LocalPlayerId }, (long)Math.Floor(_predictClock) + 1);

    /// <summary>Queues ship info to take effect when the render clock reaches its tick.</summary>
    public void EnqueueShipInfo(ShipInfo info) => _pendingShipInfos.Add(info);

    /// <summary>Creates the ship, or refreshes its hull, guns, skills, and upgrades if it already exists.</summary>
    private void ApplyShipInfo(ShipInfo info)
    {
        var abilities = info.AbilityIds.Select(id => id is null ? null : AbilityRegistry.Find(id)).ToList();
        var ship = World.FindShip(info.ShipId);
        if (ship is null)
        {
            ship = World.SpawnShip(info.Position, info.Heading, info.BaseStats, info.OwnerPlayerId, abilities, info.ShipId);
            ship.Team = info.Team;
        }
        else
        {
            // Weapons bought since: a slot holding the same weapon keeps its cooldowns.
            for (var i = 0; i < Math.Min(abilities.Count, Ship.AbilitySlotCount); i++)
                ship.SetAbility((AbilitySlot)i, abilities[i]);
        }

        ship.ReplaceSkills((info.SkillIds ?? Array.Empty<string>()).Select(SkillTrees.Find).OfType<SkillDefinition>());
        var health = ship.Health;
        ship.ReplaceModifiers(info.Modifiers);
        ship.Health = health; // health comes from snapshots; don't let re-applying upgrades top it up
    }

    public void EnqueueEvents(IEnumerable<WorldEvent> events) => _pendingEvents.AddRange(events);

    public void AddSnapshot(Snapshot snapshot)
    {
        if (_snapshots.Count > 0 && snapshot.Tick <= _snapshots[^1].Tick)
            return; // late or duplicate: we've already moved past it
        _snapshots.Add(snapshot);
    }

    /// <summary>Events whose time has come since the last call, for effects and sounds.</summary>
    public IReadOnlyList<WorldEvent> TakeEvents()
    {
        var taken = _appliedEvents.ToArray();
        _appliedEvents.Clear();
        return taken;
    }

    /// <summary>Moves the render clock on and updates the world to match.</summary>
    public void Advance(double elapsedSeconds)
    {
        World.DrainEvents(); // the mirror's own bookkeeping events (spawns etc.) are noise; the server's are what count
        if (_snapshots.Count == 0)
            return;

        var latest = _snapshots[^1].Tick;
        if (!_clockStarted)
        {
            _clock = latest;
            _clockStarted = true;
        }

        _clock += elapsedSeconds * Protocol.TickRate;
        if (Math.Abs(latest - _clock) > ClockSnapTicks)
            _clock = latest;
        else
            _clock += (latest - _clock) * 0.05; // gently track the server so the delay stays steady

        RenderTick = Math.Max(_snapshots[0].Tick, _clock - InterpolationDelayTicks);
        World.SetTick((long)RenderTick);

        // A command sent now reaches the server half a round trip from now, when it's half a round trip past
        // the newest snapshot we've seen: so predict a full round trip ahead of the snapshot clock.
        var predictTarget = _clock + RoundTripSeconds * Protocol.TickRate + PredictionMarginTicks;
        if (Math.Abs(predictTarget - _predictClock) > ClockSnapTicks)
            _predictClock = predictTarget;
        else
            _predictClock += elapsedSeconds * Protocol.TickRate + (predictTarget - _predictClock) * 0.05;
        _predictClock = Math.Max(_predictClock, latest);

        ApplyDueEvents();
        ApplySnapshots();
        FlyProjectiles();
        PredictOwnShip(elapsedSeconds);
    }

    private void PredictOwnShip(double elapsedSeconds)
    {
        var ship = LocalPlayerId == 0 ? null : World.GetPlayerShip(LocalPlayerId);
        var latest = _snapshots[^1];
        if (!PredictLocalShip || ship is null || latest.Find(ship.Id) is not { } state)
        {
            if (ship is null)
                _predictor.Forget();
            _hasOwnShipFrames = false;
            return;
        }

        // Where the snapshots put the ship at the render tick, before prediction moves it.
        _ownTimelinePosition = Vector2.Lerp(ship.PreviousPosition, ship.Position, Alpha);
        _ownTimelineHeading = Angles.Lerp(ship.PreviousHeading, ship.Heading, Alpha);

        _predictor.Update(ship, state, latest.Tick, latest.AckFor(LocalPlayerId), World.Wind, _predictClock, elapsedSeconds);

        // Drawn already blended, so previous and current agree whatever the shared alpha is.
        ship.Position = ship.PreviousPosition = _predictor.Position;
        ship.Heading = ship.PreviousHeading = _predictor.Heading;
        _ownDrawnPosition = _predictor.Position;
        _ownDrawnHeading = _predictor.Heading;
        _hasOwnShipFrames = true;
        if (_predictor.Predicted is { } predicted)
        {
            ship.Speed = predicted.Speed;
            ship.Throttle = predicted.Throttle;
            ship.Rudder = predicted.Rudder;
            ship.MoveTarget = predicted.MoveTarget;
            ship.IsHoldingCourse = predicted.IsHoldingCourse;
            ship.WindDrift = predicted.WindDrift;
            ship.Anchor = predicted.Anchor;
            ship.AnchorRaiseTicksRemaining = predicted.AnchorRaiseTicksRemaining;
            ship.AnchorDropTicksRemaining = predicted.AnchorDropTicksRemaining;
        }
    }

    private void ApplyDueEvents()
    {
        // Ship info first: events and snapshots at the same tick may refer to the ship it creates.
        foreach (var info in _pendingShipInfos.Where(i => i.Tick <= RenderTick).ToList())
            ApplyShipInfo(info);
        _pendingShipInfos.RemoveAll(i => i.Tick <= RenderTick);

        var due = _pendingEvents.Where(e => e.Tick <= RenderTick).ToList();
        if (due.Count == 0)
            return;
        _pendingEvents.RemoveAll(e => e.Tick <= RenderTick);

        foreach (var e in due)
        {
            switch (e)
            {
                case ShipSunk sunk:
                    World.RemoveShip(sunk.ShipId);
                    break;
                case ProjectileSpawned spawned:
                    _projectiles[spawned.ProjectileId] = spawned;
                    if (OwnShotOffset(spawned.OwnerShipId, spawned.Position) is { } offset)
                        _shotOffsets[spawned.ProjectileId] = offset;
                    World.AddProjectile(new Projectile(spawned.ProjectileId, spawned.OwnerShipId, spawned.Team, spawned.Damage, spawned.Radius)
                    {
                        Position = spawned.Position,
                        PreviousPosition = spawned.Position,
                        Velocity = spawned.Velocity,
                        RemainingTicks = spawned.LifetimeTicks,
                    });
                    break;
                case ProjectileImpact { PassedThrough: true }:
                    break; // a piercing shot flies on
                case ProjectileImpact impact:
                    _projectiles.Remove(impact.ProjectileId);
                    _shotOffsets.Remove(impact.ProjectileId);
                    World.RemoveProjectile(impact.ProjectileId);
                    break;
                case AreaStrikeLaunched launched:
                    World.AddStrike(new AreaStrike
                    {
                        Id = launched.StrikeId,
                        OwnerShipId = launched.OwnerShipId,
                        Team = launched.Team,
                        // From the drawn hull; the shell's flight converges on the true target by itself.
                        Origin = launched.Origin + (OwnShotOffset(launched.OwnerShipId, launched.Origin) ?? Vector2.Zero),
                        Target = launched.Target,
                        Radius = launched.Radius,
                        Damage = launched.Damage,
                        LaunchTick = launched.Tick,
                        ImpactTick = launched.ImpactTick,
                    });
                    break;
                case AreaStrikeImpact impact:
                    World.RemoveStrike(impact.StrikeId);
                    break;
                case AreaDiscovered discovered:
                    World.Discovery.Reveal(discovered.Team, discovered.Cells);
                    break;
                case RunEnded:
                    World.EndRun();
                    break;
                case ContractsOffered offered:
                    World.Trade.SetOffers(offered.IslandId, offered.Offers);
                    break;
                case ContractPurchased purchased:
                    World.FindShip(purchased.ShipId)?.LoadCargo(new CargoLot(purchased.Contract, purchased.Contract.CargoUnits));
                    break;
                case ContractDelivered delivered:
                    World.FindShip(delivered.ShipId)?.UnloadCargo(delivered.ContractId);
                    break;
                case CargoDropped dropped:
                    World.Trade.AddCrate(dropped.Position, dropped.Cargo, dropped.CrateId);
                    break;
                case CargoRecovered recovered:
                    if (World.Trade.FindCrate(recovered.CrateId) is { } crate)
                    {
                        World.Trade.RemoveCrate(crate.Id);
                        World.FindShip(recovered.ShipId)?.LoadCargo(crate.Cargo);
                    }
                    break;
            }
            _appliedEvents.Add(e);
        }
    }

    private void ApplySnapshots()
    {
        // The newest snapshot at or before the render tick, and the one after it.
        var olderIndex = _snapshots.FindLastIndex(s => s.Tick <= RenderTick);
        if (olderIndex < 0)
            olderIndex = 0;
        var older = _snapshots[olderIndex];
        var newer = olderIndex + 1 < _snapshots.Count ? _snapshots[olderIndex + 1] : older;

        // Everything older than the pair is history.
        if (olderIndex > 0)
            _snapshots.RemoveRange(0, olderIndex);

        Alpha = newer.Tick == older.Tick ? 1f : (float)Math.Clamp((RenderTick - older.Tick) / (newer.Tick - older.Tick), 0, 1);

        // World-level state (gold, waves, island timers) from the newer snapshot, so it's never behind the ships
        // and events that have already landed; per-ship state blends between the two.
        ApplyHeader(newer);
        foreach (var ship in World.Ships)
        {
            var from = older.Find(ship.Id);
            var to = newer.Find(ship.Id) ?? from;
            from ??= to;
            if (from is null || to is null)
                continue;

            ship.PreviousPosition = from.Position;
            ship.Position = to.Position;
            ship.PreviousHeading = from.Heading;
            ship.Heading = to.Heading;
            ApplyDiscrete(ship, from);
        }
    }

    private void ApplyHeader(Snapshot snapshot)
    {
        World.Wind = snapshot.Wind;
        World.Waves?.Restore(snapshot.Waves);
        if (snapshot.RunOver)
            World.EndRun();

        foreach (var p in snapshot.Players)
        {
            var player = World.GetOrAddPlayer(p.PlayerId);
            player.Gold = p.Gold;
            player.Kills = p.Kills;
            player.RespawnTicksRemaining = p.RespawnTicks;
        }

        foreach (var island in World.Islands)
            World.SetPlunderCooldown(island.Id, 0);
        foreach (var (islandId, ticks) in snapshot.IslandCooldowns)
            World.SetPlunderCooldown(islandId, ticks);
    }

    private static void ApplyDiscrete(Ship ship, ShipState state)
    {
        ship.Speed = state.Speed;
        ship.Health = state.Health;
        ship.Throttle = state.Throttle;
        ship.Rudder = state.Rudder;
        ship.Anchor = state.Anchor;
        ship.AnchorRaiseTicksRemaining = state.AnchorRaiseTicks;
        ship.PlunderIslandId = state.PlunderIslandId;
        ship.PlunderTicks = state.PlunderTicks;
        ship.Stance = state.Stance;
        ship.MoveTarget = state.MoveTarget;
        for (var i = 0; i < Ship.AbilitySlotCount; i++)
        {
            var channels = state.Cooldowns[i];
            if (ship.Abilities[i] is not { } ability || channels is null)
                continue;
            for (var c = 0; c < channels.Length; c++)
                ability.Restore(c, channels[c].Remaining, channels[c].Duration);
        }
    }

    private void FlyProjectiles()
    {
        foreach (var (id, spawned) in _projectiles.ToList())
        {
            var flown = RenderTick - spawned.Tick;
            if (flown >= spawned.LifetimeTicks)
            {
                _projectiles.Remove(id);
                _shotOffsets.Remove(id);
                World.RemoveProjectile(id);
                continue;
            }

            var projectile = World.Projectiles.FirstOrDefault(p => p.Id == id);
            if (projectile is null)
                continue;
            // Already at the render tick's position, so it needs no blending: previous and current agree.
            var position = spawned.Position + spawned.Velocity * (float)(Math.Max(0, flown) * SimConstants.TickDelta);
            if (_shotOffsets.TryGetValue(id, out var offset))
            {
                var t = (float)Math.Clamp(flown / ShotConvergeTicks, 0, 1);
                position += offset * (1f - t * t * (3f - 2f * t)); // smoothstep out
            }
            projectile.PreviousPosition = position;
            projectile.Position = position;
        }
    }

    /// <summary>
    /// For a shot fired by our own (predicted) ship: how far to move its starting point so it leaves the hull where
    /// the hull is drawn, keeping its place relative to the hull, turned by any difference in heading. Null for
    /// anyone else's shots.
    /// </summary>
    private Vector2? OwnShotOffset(int ownerShipId, Vector2 origin)
    {
        if (!_hasOwnShipFrames || World.GetPlayerShip(LocalPlayerId) is not { } ship || ship.Id != ownerShipId)
            return null;
        var turn = Matrix3x2.CreateRotation(Angles.Delta(_ownTimelineHeading, _ownDrawnHeading));
        var drawnOrigin = _ownDrawnPosition + Vector2.Transform(origin - _ownTimelinePosition, turn);
        return drawnOrigin - origin;
    }

    private static World CreateWorld(Vector2 size, Vector2 wind)
    {
        // A WaveDirector here only holds the server's counters for display; this world never steps.
        var world = new World(size) { Wind = wind, Waves = new WaveDirector(seed: 0) };
        foreach (var island in Archipelago.CreateIslands())
            world.AddIsland(island);
        return world;
    }
}
