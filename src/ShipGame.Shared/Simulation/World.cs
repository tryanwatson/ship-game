using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Simulation;

/// <summary>
/// The authoritative game state. Advances only via <see cref="Step"/>, and only changes in
/// response to queued commands, so it can run unchanged on a dedicated server.
/// </summary>
public sealed class World
{
    private readonly List<Ship> _ships = new();
    private readonly List<Projectile> _projectiles = new();
    private readonly List<AreaStrike> _strikes = new();
    private readonly List<Island> _islands = new();
    private readonly Dictionary<int, int> _plunderCooldowns = new();
    private readonly List<WorldEvent> _events = new();
    private readonly Dictionary<int, int> _lastSightCell = new();
    private readonly Queue<Command> _pendingCommands = new();
    private readonly Dictionary<int, PlayerState> _players = new();
    private int _nextEntityId = 1;

    /// <summary>
    /// Open water beyond the playable area. Ships can't be ordered into it, but can sail through it to finish a
    /// turn; without it, any maneuver near the edge would need to loop through the wall. Covers the widest loop
    /// a ship makes past its target (about two turning radii at full sail).
    /// </summary>
    public const float OutOfBoundsMargin = 8f;

    public World(Vector2 worldSize)
    {
        WorldSize = worldSize;
        Discovery = new Discovery(worldSize);
    }

    /// <summary>What each team has seen of the map. Player ships reveal it as they sail.</summary>
    public Discovery Discovery { get; }

    /// <summary>Size of the playable area. Move targets are clamped to it; ships may drift into the margin.</summary>
    public Vector2 WorldSize { get; }

    public const float DefaultWindSpeed = 0.75f;

    /// <summary>
    /// The direction the wind blows toward, scaled to how fast it drifts a ship that isn't making way
    /// (world units per second).
    /// </summary>
    public Vector2 Wind { get; set; } = Compass.Direction(Compass.SouthWest) * DefaultWindSpeed;

    public long Tick { get; private set; }

    public IReadOnlyList<Ship> Ships => _ships;

    public IReadOnlyList<Projectile> Projectiles => _projectiles;

    public IReadOnlyList<Island> Islands => _islands;

    /// <summary>Shells in the air.</summary>
    public IReadOnlyList<AreaStrike> Strikes => _strikes;

    /// <summary>Lobs a shell from <paramref name="owner"/> that lands on <paramref name="target"/> after <paramref name="flightTicks"/>.</summary>
    public AreaStrike LaunchStrike(Ship owner, Vector2 target, float radius, float damage, int flightTicks)
    {
        var strike = new AreaStrike
        {
            Id = _nextEntityId++,
            OwnerShipId = owner.Id,
            Team = owner.Team,
            Origin = owner.Position,
            Target = target,
            Radius = radius,
            Damage = damage,
            LaunchTick = Tick,
            ImpactTick = Tick + flightTicks,
        };
        _strikes.Add(strike);
        Emit(new AreaStrikeLaunched(Tick, strike.Id, strike.OwnerShipId, strike.Team, strike.Origin, strike.Target, radius, damage, strike.ImpactTick));
        return strike;
    }

    /// <summary>Adds an already-launched strike, for a client mirroring the server's shells.</summary>
    public void AddStrike(AreaStrike strike) => _strikes.Add(strike);

    public bool RemoveStrike(int id) => _strikes.RemoveAll(s => s.Id == id) > 0;

    public void AddIsland(Island island) => _islands.Add(island);

    /// <summary>Ticks until <paramref name="island"/> can be plundered again; 0 when it's ripe.</summary>
    public int PlunderCooldownTicks(Island island) => _plunderCooldowns.GetValueOrDefault(island.Id);

    public void StartPlunderCooldown(Island island, int ticks) => _plunderCooldowns[island.Id] = ticks;

    /// <summary>Distance from a point to the nearest shore; 0 on land, infinity with no islands.</summary>
    public float DistanceToLand(Vector2 point)
    {
        var nearest = float.PositiveInfinity;
        foreach (var island in _islands)
        {
            if (Vector2.Distance(point, island.Center) - island.BoundingRadius > nearest)
                continue;
            nearest = MathF.Min(nearest, island.DistanceTo(point));
        }
        return nearest;
    }

    public IReadOnlyDictionary<int, PlayerState> Players => _players;

    /// <summary>Sends pirates in waves when set; null for worlds that place their own ships (tests, sandboxes).</summary>
    public WaveDirector? Waves { get; set; }

    /// <summary>
    /// Whether players' shots hurt other players (PvP). Pirates never hurt each other either way, and no ship
    /// ever hurts itself.
    /// </summary>
    public bool FriendlyFire { get; set; }

    /// <summary>Whether a shot from <paramref name="attacker"/> (of <paramref name="attackerTeam"/>) can damage <paramref name="target"/>.</summary>
    public bool CanDamage(int attackerShipId, Team attackerTeam, Ship target) =>
        target.Id != attackerShipId
        && (target.Team != attackerTeam || (FriendlyFire && attackerTeam == Team.Players));

    /// <summary>True once every player was sunk at the same time. Nothing respawns and no more waves come.</summary>
    public bool IsRunOver { get; private set; }

    public void EndRun()
    {
        if (IsRunOver)
            return;
        IsRunOver = true;
        foreach (var player in _players.Values)
        {
            player.RespawnTicksRemaining = 0;
            player.LostShip = null;
        }
        Emit(new RunEnded(Tick));
    }

    /// <summary>Records an event for <see cref="DrainEvents"/>.</summary>
    public void Emit(WorldEvent worldEvent) => _events.Add(worldEvent);

    /// <summary>
    /// Everything that happened since the last call, in order. The owner of the world (the server, or the local
    /// session) drains after each step; events otherwise accumulate.
    /// </summary>
    public IReadOnlyList<WorldEvent> DrainEvents()
    {
        var drained = _events.ToArray();
        _events.Clear();
        return drained;
    }

    /// <summary>Changes a player's gold (negative to spend) and announces it.</summary>
    public void AddGold(int playerId, int delta)
    {
        var player = GetOrAddPlayer(playerId);
        player.Gold += delta;
        Emit(new GoldChanged(Tick, playerId, player.Gold, delta));
    }

    public PlayerState GetOrAddPlayer(int playerId)
    {
        if (!_players.TryGetValue(playerId, out var player))
            _players[playerId] = player = new PlayerState(playerId);
        return player;
    }

    public Ship SpawnShip(
        Vector2 position,
        float heading,
        ShipStats stats,
        int? ownerPlayerId = null,
        IReadOnlyList<Ability?>? abilities = null,
        int? id = null)
    {
        // Explicit ids are for clients mirroring the server, which hands out the real ones.
        var shipId = id ?? _nextEntityId++;
        _nextEntityId = Math.Max(_nextEntityId, shipId + 1);
        var ship = new Ship(shipId, ownerPlayerId, stats, abilities)
        {
            Position = position,
            Heading = heading,
            PreviousPosition = position,
            PreviousHeading = heading,
        };
        _ships.Add(ship);
        if (ownerPlayerId is { } playerId)
            GetOrAddPlayer(playerId);
        Emit(new ShipSpawned(Tick, ship.Id));
        return ship;
    }

    public Ship? FindShip(int id) => _ships.Find(s => s.Id == id);

    /// <summary>Removes a ship outright (no sinking, no rewards): for disconnects and for mirroring the server.</summary>
    public bool RemoveShip(int id)
    {
        _lastSightCell.Remove(id);
        return _ships.RemoveAll(s => s.Id == id) > 0;
    }

    /// <summary>Adds an already-built projectile, for a client flying the server's shots.</summary>
    public void AddProjectile(Projectile projectile) => _projectiles.Add(projectile);

    public bool RemoveProjectile(int id) => _projectiles.RemoveAll(p => p.Id == id) > 0;

    /// <summary>Takes a player out of the run (they left): their ship goes and they stop counting toward wave size.</summary>
    public void RemovePlayer(int playerId)
    {
        if (GetPlayerShip(playerId) is { } ship)
        {
            RemoveShip(ship.Id);
            Emit(new ShipSunk(Tick, ship.Id, null));
        }
        _players.Remove(playerId);
    }

    /// <summary>Sets an island's plunder cooldown directly (0 clears it), for mirroring the server.</summary>
    public void SetPlunderCooldown(int islandId, int ticks)
    {
        if (ticks > 0)
            _plunderCooldowns[islandId] = ticks;
        else
            _plunderCooldowns.Remove(islandId);
    }

    public IReadOnlyDictionary<int, int> PlunderCooldowns => _plunderCooldowns;

    /// <summary>Sets the tick counter, for a client mirroring the server's clock.</summary>
    public void SetTick(long tick) => Tick = tick;

    public Projectile SpawnProjectile(Ship owner, Vector2 position, Vector2 velocity, float damage, int lifetimeTicks,
        float radius = Projectile.DefaultRadius)
    {
        var projectile = new Projectile(_nextEntityId++, owner.Id, owner.Team, damage, radius)
        {
            Position = position,
            PreviousPosition = position,
            Velocity = velocity,
            RemainingTicks = lifetimeTicks,
        };
        _projectiles.Add(projectile);
        Emit(new ProjectileSpawned(Tick, projectile.Id, owner.Id, owner.Team, position, velocity, damage, lifetimeTicks, radius));
        return projectile;
    }

    public Ship? GetPlayerShip(int playerId) => _ships.Find(s => s.OwnerPlayerId == playerId);

    /// <summary>Queues a command to be applied at the start of the next tick.</summary>
    public void Enqueue(Command command) => _pendingCommands.Enqueue(command);

    public void Step()
    {
        const float dt = SimConstants.TickDelta;

        foreach (var islandId in _plunderCooldowns.Keys.ToList())
        {
            if (--_plunderCooldowns[islandId] <= 0)
                _plunderCooldowns.Remove(islandId);
        }

        foreach (var ship in _ships)
        {
            ship.PreviousPosition = ship.Position;
            ship.PreviousHeading = ship.Heading;
            Anchoring.Tick(ship);

            foreach (var ability in ship.Abilities)
                ability?.TickCooldown();
        }

        foreach (var projectile in _projectiles)
            projectile.PreviousPosition = projectile.Position;

        while (_pendingCommands.TryDequeue(out var command))
            Apply(command);

        foreach (var ship in _ships)
            ship.Behavior?.Update(this, ship);

        foreach (var ship in _ships)
            ShipMovement.Step(ship, dt, Wind);

        ShipMovement.ResolveCollisions(_ships);

        foreach (var ship in _ships)
        {
            if (IslandCollision.Resolve(ship, _islands))
                Emit(new ShipGrounded(Tick, ship.Id));
        }

        RevealMap();

        foreach (var ship in _ships)
            ShipMovement.ClampToBounds(ship, new Vector2(-OutOfBoundsMargin), WorldSize + new Vector2(OutOfBoundsMargin));

        StepProjectiles(dt);
        StepStrikes();

        Plundering.Step(this);

        ResolveSinkings();
        Respawning.Step(this);

        Waves?.Update(this);

        Tick++;
    }

    private void ResolveSinkings()
    {
        foreach (var victim in _ships)
        {
            if (!victim.IsSunk || victim.LastHitByShipId is not { } killerId)
                continue;

            // Credit the kill even if the killer went down in the same exchange.
            var killer = _ships.Find(s => s.Id == killerId);
            if (killer is not null && CanDamage(killer.Id, killer.Team, victim))
                KillRewards.Grant(this, killer);
        }

        foreach (var victim in _ships)
        {
            if (!victim.IsSunk)
                continue;
            Emit(new ShipSunk(Tick, victim.Id, victim.LastHitByShipId));
            if (victim.OwnerPlayerId is { } playerId)
                Respawning.OnPlayerSunk(this, victim, GetOrAddPlayer(playerId));
        }

        foreach (var ship in _ships)
        {
            if (ship.IsSunk)
                _lastSightCell.Remove(ship.Id);
        }
        _ships.RemoveAll(s => s.IsSunk);
    }

    /// <summary>
    /// Each player ship reveals the map around it for its team; new discoveries are announced once per team per tick.
    /// Sight is taken from the center of the ship's current cell and only rescanned when it enters a new cell, since
    /// nothing new can come into view before then.
    /// </summary>
    private void RevealMap()
    {
        Dictionary<Team, List<int>>? revealed = null;
        foreach (var ship in _ships)
        {
            if (ship.OwnerPlayerId is null)
                continue; // pirates don't map anything
            if (Discovery.CellAt(ship.Position) is not { } cell
                || (_lastSightCell.TryGetValue(ship.Id, out var last) && last == cell))
                continue;
            _lastSightCell[ship.Id] = cell;

            var cells = Discovery.RevealAround(ship.Team, Discovery.CellCenter(cell));
            if (cells.Count == 0)
                continue;
            revealed ??= new Dictionary<Team, List<int>>();
            if (!revealed.TryGetValue(ship.Team, out var list))
                revealed[ship.Team] = list = new List<int>();
            list.AddRange(cells);
        }
        if (revealed is null)
            return;
        foreach (var (team, cells) in revealed)
            Emit(new AreaDiscovered(Tick, team, cells));
    }

    /// <summary>Bursts every shell that's due: hurts each hostile hull within its blast radius.</summary>
    private void StepStrikes()
    {
        Span<Vector2> hull = stackalloc Vector2[HullShape.PointCount];
        foreach (var strike in _strikes)
        {
            if (strike.ImpactTick > Tick)
                continue;

            foreach (var ship in _ships)
            {
                if (ship.IsSunk || !CanDamage(strike.OwnerShipId, strike.Team, ship))
                    continue;
                HullShape.GetWorldOutline(ship.Position, ship.Heading, ship.Stats, hull);
                if (Geometry.DistanceToConvex(hull, strike.Target) > strike.Radius)
                    continue;
                ship.Health = MathF.Max(0f, ship.Health - strike.Damage);
                ship.LastHitByShipId = strike.OwnerShipId;
            }
            Emit(new AreaStrikeImpact(Tick, strike.Id, strike.Target, strike.Radius));
        }
        _strikes.RemoveAll(s => s.ImpactTick <= Tick);
    }

    private void StepProjectiles(float dt)
    {
        foreach (var projectile in _projectiles)
        {
            var from = projectile.Position;
            projectile.Position += projectile.Velocity * dt;
            projectile.RemainingTicks--;

            if (LineHitsLand(from, projectile.Position, projectile.Radius))
            {
                projectile.RemainingTicks = 0;
                Emit(new ProjectileImpact(Tick, projectile.Id, null));
                continue;
            }

            foreach (var ship in _ships)
            {
                if (ship.IsSunk || !CanDamage(projectile.OwnerShipId, projectile.Team, ship))
                    continue;

                if (HullShape.SegmentHits(ship, from, projectile.Position, projectile.Radius))
                {
                    ship.Health = MathF.Max(0f, ship.Health - projectile.Damage);
                    ship.LastHitByShipId = projectile.OwnerShipId;
                    projectile.RemainingTicks = 0;
                    Emit(new ProjectileImpact(Tick, projectile.Id, ship.Id));
                    break;
                }
            }
        }

        _projectiles.RemoveAll(p => p.RemainingTicks <= 0);
    }

    /// <summary>Whether a ball of <paramref name="radius"/> travelling from <paramref name="from"/> to <paramref name="to"/> would strike land.</summary>
    public bool LineHitsLand(Vector2 from, Vector2 to, float radius)
    {
        foreach (var island in _islands)
        {
            if (Geometry.DistanceToSegment(island.Center, from, to) > island.BoundingRadius + radius)
                continue;
            if (Geometry.SegmentTouchesConvex(island.Outline, from, to, radius))
                return true;
        }
        return false;
    }

    private void Apply(Command command)
    {
        var reason = TryApply(command);
        if (reason is { } rejected)
            Emit(new CommandRejected(Tick, command.PlayerId, command, rejected));
    }

    /// <summary>Applies a command, or explains why not. Null means it went through.</summary>
    private RejectionReason? TryApply(Command command)
    {
        // Commands only ever act on the issuing player's own ship; this is also the server-side ownership check.
        var ship = GetPlayerShip(command.PlayerId);
        if (ship is null)
            return RejectionReason.NoShip;

        switch (command)
        {
            case MoveCommand when ship.IsAnchored:
                return RejectionReason.Anchored; // held fast: no sailing anywhere until the anchor is up
            case MoveCommand move:
                ship.MoveTarget = Vector2.Clamp(move.Target, Vector2.Zero, WorldSize);
                ship.IsHoldingCourse = false;
                ship.Rudder = 0;
                if (ship.Throttle == 0)
                    ship.Throttle = ShipMovement.AutopilotThrottle;
                return null;
            case StopCommand:
                ship.MoveTarget = null;
                ship.IsHoldingCourse = false;
                ship.Throttle = 0;
                return null;
            case AdjustThrottleCommand adjust:
                ship.Throttle = Math.Clamp(ship.Throttle + adjust.Delta, 0, ShipMovement.ThrottleLevels);
                return null;
            case AnchorKeyCommand { Pressed: false }:
                Anchoring.ReleaseKey(ship);
                return null;
            case AnchorKeyCommand when ship.Anchor == AnchorState.Raising:
                return RejectionReason.AnchorBusy;
            case AnchorKeyCommand:
                Anchoring.PressKey(ship);
                return null;
            case ChoosePlunderCommand:
                return Shipyards.TryChoosePlunder(this, ship);
            case PurchaseUpgradeCommand purchase:
                return Shipyards.ToRejection(Shipyards.TryPurchase(this, ship, purchase.UpgradeId));
            case SetRudderCommand rudder:
                ship.Rudder = Math.Clamp(rudder.Rudder, -1, 1);
                if (ship.Rudder != 0)
                {
                    ship.MoveTarget = null;
                    ship.IsHoldingCourse = false;
                }
                return null;
            case CastAbilityCommand cast:
                return CastAbility(ship, cast.Slot, cast.Target);
            default:
                return null;
        }
    }

    /// <summary>
    /// Casts a ship's ability if the slot is filled and off cooldown. The single entry point for both player
    /// commands and NPC behaviors, so both play by the same rules.
    /// </summary>
    public bool TryCastAbility(Ship ship, AbilitySlot slot, Vector2 target) => CastAbility(ship, slot, target) is null;

    private RejectionReason? CastAbility(Ship ship, AbilitySlot slot, Vector2 target)
    {
        if (!Enum.IsDefined(slot))
            return RejectionReason.InvalidSlot;

        var ability = ship.GetAbility(slot);
        if (ability is null)
            return RejectionReason.EmptySlot;
        var channel = ability.Definition.ChannelFor(ship, target);
        if (!ability.IsChannelReady(channel))
            return RejectionReason.OnCooldown;

        if (!ability.Definition.Cast(this, ship, target))
            return RejectionReason.CastFailed;

        ability.StartCooldown(channel, ship.Stats.CooldownSpeed);
        Emit(new AbilityCast(Tick, ship.Id, slot, ability.DurationTicks(channel), channel));
        return null;
    }

}
