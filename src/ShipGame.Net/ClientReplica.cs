using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
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
/// answers at once. Its cannonballs fly on that same clock: they leave the guns the moment the key goes (see
/// <see cref="OnCommandSent"/>), keep pace with the hull, and strike ships where they're drawn here, as the server
/// will find too (it tests our shots against where we saw everyone, see <see cref="Ship.ShotRewindTicks"/>).
/// Everything else, including the ship's health, stays on the server's timeline.
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
    /// Our own shots are fired here before the server fires them. When the server's arrive, ours blend onto their
    /// paths (any difference is the prediction's error, normally a hair) over this many ticks.
    /// </summary>
    public const double ShotConvergeTicks = 6;

    // A shot we fired that the server hasn't answered for this long past the round trip was refused.
    private const double UnansweredShotTicks = 10;

    // How long to keep track of a shot after it's gone here, for the server's word on it to arrive.
    private const double ForgetShotTicks = 2 * SimConstants.TickRate;

    // Server shots match ours if they fly this close to the same way.
    private const float MatchVelocityTolerance = 1f;

    /// <summary>A cannonball in flight, as drawn: from <see cref="Origin"/> at <see cref="StartTick"/> on the render clock.</summary>
    private sealed class Flight
    {
        public required Projectile Projectile;
        public required Vector2 Origin;
        public required Vector2 Velocity;
        public required double StartTick;
        public required double EndTick;
        public Vector2 Offset;
        public double OffsetTick;

        /// <summary>Ours, fired here: we work out what it hits. Fired before the server's copy arrives, which <see cref="ServerId"/> names once it has.</summary>
        public bool Predicted;
        public int? ServerId;
        public double FiredAtPredictTick;
        public int PierceRemaining;
        public bool IgnoresLand;
        public List<int> ShipsHit = new();

        /// <summary>Hit or spent here; kept a while so the server's word on it can be matched and dropped.</summary>
        public bool Gone;
        public Vector2 LastPosition;

        public Vector2 At(double tick)
        {
            var position = Origin + Velocity * (float)(Math.Max(0, tick - StartTick) * SimConstants.TickDelta);
            if (Offset != Vector2.Zero)
            {
                var t = (float)Math.Clamp((tick - OffsetTick) / ShotConvergeTicks, 0, 1);
                position += Offset * (1f - t * t * (3f - 2f * t)); // smoothstep out
            }
            return position;
        }
    }

    private readonly List<Snapshot> _snapshots = new();
    private readonly List<WorldEvent> _pendingEvents = new();
    private readonly List<ShipInfo> _pendingShipInfos = new();
    private readonly List<WorldEvent> _appliedEvents = new();
    private readonly Dictionary<int, Flight> _flights = new();
    private readonly Dictionary<int, int> _serverToLocal = new();
    private readonly Dictionary<(AbilitySlot, int), double> _gunsFiredUntil = new();
    private int _nextLocalShotId = -1;

    // Last frame's local ship: where the server's timeline had it, and where it was drawn (predicted).
    private bool _hasOwnShipFrames;
    private Vector2 _ownTimelinePosition;
    private float _ownTimelineHeading;
    private Vector2 _ownDrawnPosition;
    private float _ownDrawnHeading;
    private double _clock;
    private bool _clockStarted;
    private double _predictClock;
    private LocalShipPredictor _predictor = new(Regions.StartSize);

    /// <summary>A region the server has sailed into whose first snapshot hasn't come yet: the predictor moves to it then.</summary>
    private RegionEntered? _regionAhead;

    public ClientReplica()
    {
        World = CreateWorld(Regions.StartSize, Vector2.Zero);
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

    /// <summary>The newest snapshot's <see cref="Snapshot.Sequence"/>; 0 before any.</summary>
    public uint LatestSnapshotSequence { get; private set; }

    /// <summary>A new run: start from an empty sea. The chart and the first region's islands follow as events.</summary>
    public void Reset(RunStart start)
    {
        World = CreateWorld(start.WorldSize, start.Wind);
        World.FriendlyFire = start.FriendlyFire;
        // Everyone opens the run choosing a card, then a weapon: the game waits for them (the cards arrive as events).
        foreach (var (playerId, name) in start.Crew ?? Array.Empty<(int, string)>())
        {
            var player = World.GetOrAddPlayer(playerId);
            player.Name = name;
            player.NeedsStartingWeapon = true;
        }
        World.SetTick(start.Tick);
        _snapshots.Clear();
        _pendingEvents.Clear();
        _pendingShipInfos.Clear();
        _flights.Clear();
        _serverToLocal.Clear();
        _gunsFiredUntil.Clear();
        _hasOwnShipFrames = false;
        _clockStarted = false;
        LatestSnapshotSequence = 0;
        _predictor = new LocalShipPredictor(start.WorldSize);
        _regionAhead = null;
        Alpha = 1f;
    }

    /// <summary>
    /// A command was just sent with this sequence number: replay it in the prediction until the server confirms it.
    /// </summary>
    public void OnCommandSent(Command command, uint sequence)
    {
        // As on the server, it's ours whatever player it names.
        _predictor.Record(sequence, command with { PlayerId = LocalPlayerId }, (long)Math.Floor(_predictClock) + 1);
        if (command is CastAbilityCommand cast)
            FireOwnGuns(cast);
    }

    /// <summary>How far ahead of the render clock our own ship (and its shots) are drawn.</summary>
    private double OwnLead => Math.Max(0, _predictClock - RenderTick);

    /// <summary>
    /// Fires our guns here, now, from the hull as drawn, for a cast the server will carry out once it arrives: the
    /// same balls it will fire, from the same place, since that's where the prediction says we'll be. Only for guns
    /// that fire straight away; a shell's flight and a pirate's wind-up already take their time.
    /// </summary>
    private void FireOwnGuns(CastAbilityCommand cast)
    {
        if (!_hasOwnShipFrames || _snapshots.Count == 0 || _snapshots[^1].Paused
            || World.GetPlayerShip(LocalPlayerId) is not { } ship
            || ship.GetAbility(cast.Slot) is not { Definition: BroadsideVolley or LongGun } ability
            || ability.Definition.WindupTicksFor(ship) > 0)
            return;

        // Reloads here are as of the render clock, behind ours; and the snapshots won't show a reload we've just
        // started until it's come back round, so remember those.
        var channel = ability.Definition.ChannelFor(ship, cast.Target);
        if (ability.RemainingTicks(channel) > OwnLead
            || _gunsFiredUntil.GetValueOrDefault((cast.Slot, channel)) > _predictClock)
            return;
        _gunsFiredUntil[(cast.Slot, channel)] =
            _predictClock + ability.Definition.CooldownTicksFor(ship) / MathF.Max(ship.Stats.CooldownSpeed, 0.01f);

        var count = World.Projectiles.Count;
        if (!ability.Definition.Cast(World, ship, cast.Target))
            return;
        foreach (var fired in World.Projectiles.Skip(count).ToList())
        {
            World.RemoveProjectile(fired);
            var id = _nextLocalShotId--;
            var projectile = new Projectile(id, ship.Id, ship.Team, fired.Damage, fired.Radius)
            {
                Position = fired.Position, PreviousPosition = fired.Position, Velocity = fired.Velocity, RemainingTicks = fired.RemainingTicks,
            };
            World.AddProjectile(projectile);
            _flights[id] = new Flight
            {
                Projectile = projectile,
                Origin = fired.Position,
                Velocity = fired.Velocity,
                StartTick = RenderTick,
                EndTick = RenderTick + fired.RemainingTicks,
                Predicted = true,
                FiredAtPredictTick = _predictClock,
                PierceRemaining = fired.PierceRemaining,
                IgnoresLand = fired.Effects.IgnoresLand,
                LastPosition = fired.Position,
            };
            _appliedEvents.Add(new ProjectileSpawned((long)Math.Round(RenderTick), id, ship.Id, ship.Team, fired.Position,
                fired.Velocity, fired.Damage, fired.RemainingTicks, fired.Radius));
        }
    }

    /// <summary>Queues ship info to take effect when the render clock reaches its tick.</summary>
    public void EnqueueShipInfo(ShipInfo info) => _pendingShipInfos.Add(info);

    /// <summary>Creates the ship, or refreshes its hull, guns, skills, cards, and upgrades if it already exists.</summary>
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

        ship.Level = info.Level;
        ship.IsBoss = info.IsBoss;
        ship.FortIslandId = info.FortIslandId;
        ship.ReplaceSkills((info.SkillIds ?? Array.Empty<string>()).Select(SkillTrees.Find).OfType<SkillDefinition>());
        var health = ship.Health;
        ship.ReplaceCards((info.Cards ?? Array.Empty<CardPick>()).Where(c => CardCatalog.Find(c.Id) is not null));
        ship.ReplaceModifiers(info.Modifiers);
        ship.Health = health; // health comes from snapshots; don't let re-applying upgrades top it up
    }

    public void EnqueueEvents(IEnumerable<WorldEvent> events)
    {
        foreach (var e in events)
        {
            // Our ship is predicted from the newest snapshot: once one from the new region is in (see AddSnapshot),
            // predict it against the new islands, not once the render clock catches up.
            if (e is RegionEntered entered)
                _regionAhead = entered;
            _pendingEvents.Add(e);
        }
    }

    public void AddSnapshot(Snapshot snapshot)
    {
        if (snapshot.Sequence <= LatestSnapshotSequence && LatestSnapshotSequence != 0)
            return; // late or duplicate: we've already moved past it
        if (_snapshots.Count > 0 && snapshot.Tick < _snapshots[^1].Tick)
            return;
        LatestSnapshotSequence = snapshot.Sequence;
        // The region changes during the step announced (tick T); snapshots from T + 1 on are taken in the new one.
        if (_regionAhead is { } region && snapshot.Tick > region.Tick)
        {
            _predictor = _predictor.ForRegion(region.Size, region.Islands);
            _regionAhead = null;
        }
        // Paused, the server sends the same tick again with a fresher header (command acks): it replaces the last.
        if (_snapshots.Count > 0 && snapshot.Tick == _snapshots[^1].Tick)
            _snapshots[^1] = snapshot;
        else
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

        // Paused for cards, the server's clock stands still, and so does ours: everything stays where it stopped,
        // and all that happened up to the pause, and what the server says meanwhile (choices, new cards), takes
        // effect at once. (Waiting on the clock would wait forever: we draw a little behind the newest snapshot.)
        var paused = _snapshots[^1].Paused;
        if (!paused)
        {
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
        }

        ApplyOwnShotEvents();
        ApplyDueEvents(paused ? latest : RenderTick);
        World.PutOutFires();
        ApplySnapshots(paused);
        World.RemoveSpentWarnings(paused ? latest : RenderTick); // the shot itself has come in as a projectile
        if (paused)
        {
            _hasOwnShipFrames = false; // drawn where the server holds it; prediction picks up again on resuming
            return;
        }
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

    /// <summary>
    /// Our own shots fly on our ship's clock, ahead of the render clock, so what the server says about them is
    /// already due when it arrives: match its shots to the ones we fired, and its hits to the ones we saw.
    /// </summary>
    private void ApplyOwnShotEvents()
    {
        if (!_hasOwnShipFrames || World.GetPlayerShip(LocalPlayerId) is not { } ship)
            return;
        for (var i = 0; i < _pendingEvents.Count; i++)
        {
            switch (_pendingEvents[i])
            {
                case ProjectileSpawned spawned when spawned.OwnerShipId == ship.Id:
                    TakeOwnShot(spawned);
                    break;
                case ProjectileImpact impact when _serverToLocal.ContainsKey(impact.ProjectileId)
                                                  || (_flights.TryGetValue(impact.ProjectileId, out var flight) && flight.Projectile.OwnerShipId == ship.Id):
                    TakeOwnImpact(impact);
                    break;
                default:
                    continue;
            }
            _pendingEvents.RemoveAt(i--);
        }
    }

    private void TakeOwnShot(ProjectileSpawned spawned)
    {
        // On our clock, it was fired as far ahead of the render clock as our ship is drawn.
        var start = spawned.Tick - OwnLead;
        var mine = _flights.Values
            .Where(f => f.Predicted && f.ServerId is null && f.Projectile.Radius == spawned.Radius
                        && Vector2.Distance(f.Velocity, spawned.Velocity) <= MatchVelocityTolerance)
            .MinBy(f => Vector2.DistanceSquared(f.Origin, spawned.Position));
        if (mine is not null)
        {
            // One we fired: from here on it follows the server's path, blending across from ours.
            mine.ServerId = spawned.ProjectileId;
            _serverToLocal[spawned.ProjectileId] = mine.Projectile.Id;
            var drawn = mine.At(RenderTick);
            mine.Origin = spawned.Position;
            mine.Velocity = spawned.Velocity;
            mine.StartTick = start;
            mine.EndTick = start + spawned.LifetimeTicks;
            mine.Offset = drawn - mine.At(RenderTick);
            mine.OffsetTick = RenderTick;
            return;
        }

        // One we didn't (an echo, a ricochet): it flies on our clock all the same, and the server says what it hits.
        var projectile = new Projectile(spawned.ProjectileId, spawned.OwnerShipId, spawned.Team, spawned.Damage, spawned.Radius)
        {
            Velocity = spawned.Velocity, RemainingTicks = spawned.LifetimeTicks,
        };
        var flight = new Flight
        {
            Projectile = projectile, Origin = spawned.Position, Velocity = spawned.Velocity, StartTick = start,
            EndTick = start + spawned.LifetimeTicks, ServerId = spawned.ProjectileId,
        };
        projectile.Position = projectile.PreviousPosition = flight.LastPosition = flight.At(RenderTick);
        World.AddProjectile(projectile);
        _flights[spawned.ProjectileId] = flight;
        _appliedEvents.Add(spawned with { Tick = (long)Math.Round(start) });
    }

    private void TakeOwnImpact(ProjectileImpact impact)
    {
        var id = _serverToLocal.GetValueOrDefault(impact.ProjectileId, impact.ProjectileId);
        if (!_flights.TryGetValue(id, out var flight) || flight.Gone)
            return; // we saw it strike already
        if (flight.Predicted && impact.ShipId is { } struck && flight.ShipsHit.Contains(struck))
            return;
        // Something we didn't see coming (or a shot we don't follow): take the server's word for it.
        if (impact.ShipId is { } hit)
            flight.ShipsHit.Add(hit);
        _appliedEvents.Add(impact with { Tick = (long)Math.Round(RenderTick), ProjectileId = id });
        if (!impact.PassedThrough)
            Ground(flight);
    }

    /// <summary>The shot's gone here; its record stays a while for the server's word on it.</summary>
    private void Ground(Flight flight)
    {
        flight.Gone = true;
        World.RemoveProjectile(flight.Projectile);
    }

    /// <param name="upTo">Apply what's stamped up to this tick: the render tick, or while paused, the tick the server stopped at.</param>
    private void ApplyDueEvents(double upTo)
    {
        bool Due(long tick) => tick <= upTo;

        // Ship info first: events and snapshots at the same tick may refer to the ship it creates.
        foreach (var info in _pendingShipInfos.Where(i => Due(i.Tick)).ToList())
            ApplyShipInfo(info);
        _pendingShipInfos.RemoveAll(i => Due(i.Tick));

        var due = _pendingEvents.Where(e => Due(e.Tick)).ToList();
        if (due.Count == 0)
            return;
        _pendingEvents.RemoveAll(e => Due(e.Tick));

        foreach (var e in due)
        {
            switch (e)
            {
                case ShipSunk sunk:
                    World.RemoveShip(sunk.ShipId);
                    break;
                case ShipHidden hidden:
                    World.RemoveShip(hidden.ShipId);
                    break;
                case ProjectileSpawned spawned:
                {
                    var projectile = new Projectile(spawned.ProjectileId, spawned.OwnerShipId, spawned.Team, spawned.Damage, spawned.Radius)
                    {
                        Position = spawned.Position,
                        PreviousPosition = spawned.Position,
                        Velocity = spawned.Velocity,
                        RemainingTicks = spawned.LifetimeTicks,
                    };
                    World.AddProjectile(projectile);
                    _flights[spawned.ProjectileId] = new Flight
                    {
                        Projectile = projectile, Origin = spawned.Position, Velocity = spawned.Velocity, StartTick = spawned.Tick,
                        EndTick = spawned.Tick + spawned.LifetimeTicks, LastPosition = spawned.Position,
                    };
                    break;
                }
                case ProjectileImpact { PassedThrough: true }:
                    break; // a piercing shot flies on
                case ProjectileImpact impact:
                    _flights.Remove(impact.ProjectileId);
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
                case ShotWarned warned:
                    World.AddWarning(new ShotWarning
                    {
                        ShipId = warned.ShipId,
                        Slot = warned.Slot,
                        Channel = warned.Channel,
                        Target = warned.Target,
                        StartTick = warned.Tick,
                        FireTick = warned.FireTick,
                    });
                    break;
                case AreaDiscovered discovered:
                    World.Discovery.Reveal(discovered.Team, discovered.Cells);
                    break;
                case RunEnded ended:
                    World.EndRun(ended.Victory);
                    break;
                case FortressTaken taken:
                    if (World.FindIsland(taken.IslandId) is { } fortress)
                        World.TakeFortress(fortress);
                    break;
                case CardsOffered offered:
                    World.GetOrAddPlayer(offered.PlayerId).CardOffers.Add(offered.Offer);
                    break;
                case CardChosen chosen:
                    CardRewards.ApplyChosen(World, chosen.PlayerId, chosen.Card);
                    break;
                case StartingWeaponChosen chosen:
                    World.GetOrAddPlayer(chosen.PlayerId).NeedsStartingWeapon = false;
                    break;
                case FireStarted fire:
                    World.AddFire(new FireZone
                    {
                        Id = fire.FireId, OwnerShipId = fire.OwnerShipId, Team = fire.Team, Position = fire.Position, Radius = fire.Radius,
                        Dps = fire.Dps, StartTick = fire.Tick, EndTick = fire.EndTick,
                    });
                    break;
                case VoyageCharted charted when World.Director is { } director:
                    director.Chart = charted.Chart;
                    break;
                case RegionEntered entered:
                    EnterRegion(entered);
                    break;
                case CardsRerolled rerolled:
                    CardRewards.ApplyRerolled(World, rerolled.PlayerId, rerolled.Offer, rerolled.Rerolls);
                    break;
            }
            _appliedEvents.Add(e);
        }
    }

    /// <param name="paused">The server is paused: its counters come from the newest snapshot, not the one at our (stopped) clock.</param>
    private void ApplySnapshots(bool paused)
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
        ApplyHeader(paused ? _snapshots[^1] : newer);
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
            ApplyDiscrete(ship, from, older.Find(ship.Id) is null ? newer.Tick : older.Tick);
        }
    }

    private void ApplyHeader(Snapshot snapshot)
    {
        World.Wind = snapshot.Wind;
        // The newer snapshot runs ahead of what's drawn: until the crew has sailed into a new stop's sea here (its
        // RegionEntered applied at the render tick), the run's progress stays at the old stop, matching the islands.
        var sailingAhead = World.Director is { } director && snapshot.Run.NodeId != director.NodeId
                           && _pendingEvents.Any(e => e is RegionEntered entered && entered.NodeId == snapshot.Run.NodeId);
        if (!sailingAhead)
            World.Director?.Restore(snapshot.Run);
        if (snapshot.RunOver)
            World.EndRun(snapshot.Victory);

        foreach (var p in snapshot.Players)
        {
            var player = World.GetOrAddPlayer(p.PlayerId);
            player.Gold = p.Gold;
            player.Kills = p.Kills;
            player.RespawnTicksRemaining = p.RespawnTicks;
            player.CourseVote = p.CourseVote;
            player.ExtraLives = p.ExtraLives;
            player.PacksBoughtHere = p.PacksBoughtHere;
        }

        World.SetPlunderedIslands(snapshot.PlunderedIslands);
    }

    private static void ApplyDiscrete(Ship ship, ShipState state, long stateTick)
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
        ship.ReplaceStatuses(state.Statuses.Select(s => new StatusEffect
        {
            Id = s.Id, Stacks = s.Stacks, Power = s.Power, UntilTick = stateTick + s.RemainingTicks,
        }));
        if (state.Tallies.Length > 0 || ship.Tallies.Count > 0)
            ship.ReplaceTallies(state.Tallies);
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
        foreach (var (id, flight) in _flights.ToList())
        {
            if (UnansweredByServer(flight))
                flight.Predicted = false; // never to be matched now: a late answer is a new shot
            if (flight.Gone || RenderTick >= flight.EndTick || (flight.ServerId is null && !flight.Predicted && flight.FiredAtPredictTick > 0))
            {
                // Spent: gone from the sea now, and from our records once the server's had its say.
                if (!flight.Gone)
                    Ground(flight);
                if (RenderTick >= flight.EndTick + ForgetShotTicks || flight.ServerId is null && !flight.Predicted)
                    Forget(id, flight);
                continue;
            }

            // Already at the render tick's position, so it needs no blending: previous and current agree.
            var position = flight.At(RenderTick);
            flight.Projectile.PreviousPosition = flight.Projectile.Position = position;
            if (flight.Predicted)
                StrikeAsDrawn(flight, flight.LastPosition, position);
            flight.LastPosition = position;
        }
    }

    // One we fired that the server never did: the cast was refused (out of reach of the guns, say).
    private bool UnansweredByServer(Flight flight) =>
        flight.Predicted && flight.ServerId is null
        && _predictClock - flight.FiredAtPredictTick > RoundTripSeconds * Protocol.TickRate + UnansweredShotTicks;

    private void Forget(int id, Flight flight)
    {
        _flights.Remove(id);
        if (flight.ServerId is { } serverId)
            _serverToLocal.Remove(serverId);
    }

    /// <summary>
    /// What one of our shots strikes as it moves from <paramref name="from"/> to <paramref name="to"/>, against ships
    /// where they're drawn: the server tests it against the same, so it will agree (see <see cref="Ship.ShotRewindTicks"/>).
    /// </summary>
    private void StrikeAsDrawn(Flight flight, Vector2 from, Vector2 to)
    {
        var shot = flight.Projectile;
        var hitsLand = !flight.IgnoresLand && World.LineHitsLand(from, to, shot.Radius, clearForts: true);
        var tick = (long)Math.Round(RenderTick);
        foreach (var ship in World.Ships)
        {
            if ((hitsLand && !ship.IsFort) || ship.IsSunk || flight.ShipsHit.Contains(ship.Id)
                || !World.CanDamage(shot.OwnerShipId, shot.Team, ship))
                continue;
            var position = Vector2.Lerp(ship.PreviousPosition, ship.Position, Alpha);
            var heading = Angles.Lerp(ship.PreviousHeading, ship.Heading, Alpha);
            if (!HullShape.SegmentHits(ship, position, heading, from, to, shot.Radius))
                continue;

            flight.ShipsHit.Add(ship.Id);
            var passesThrough = flight.PierceRemaining > 0;
            _appliedEvents.Add(new ProjectileImpact(tick, shot.Id, ship.Id, passesThrough));
            if (passesThrough)
            {
                flight.PierceRemaining--;
                continue;
            }
            Ground(flight);
            return;
        }

        if (hitsLand)
        {
            _appliedEvents.Add(new ProjectileImpact(tick, shot.Id, null));
            Ground(flight);
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

    /// <summary>
    /// The crew sailed on: the old region's ships, shots, and islands go, and the new islands come. (Our own ship's
    /// prediction moved over when the news arrived.)
    /// </summary>
    private void EnterRegion(RegionEntered entered)
    {
        World.EnterRegion(entered.Size, entered.Islands, entered.FirstEntityId);
        _flights.Clear();
        _serverToLocal.Clear();
        _gunsFiredUntil.Clear();
        _hasOwnShipFrames = false;
    }

    private static World CreateWorld(Vector2 size, Vector2 wind)
    {
        // A RunDirector here only holds the server's counters (and the chart) for display; this world never steps.
        var world = new World(size) { Wind = wind };
        world.Director = new RunDirector(seed: 0);
        return world;
    }
}
