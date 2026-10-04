using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Stats;
using ShipGame.Shared.Trading;
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
    private readonly List<ShotWarning> _warnings = new();
    private readonly List<FireZone> _fires = new();
    private readonly List<PendingEcho> _echoes = new();
    private readonly List<(Ship A, Ship B)> _contacts = new();

    /// <summary>A weapon firing again a moment after it was fired (the Echo card), at a share of its damage.</summary>
    private sealed record PendingEcho(int ShipId, AbilitySlot Slot, Vector2 Target, long DueTick, float Scale);
    private readonly List<Island> _islands = new();
    private readonly HashSet<int> _plunderedIslands = new();
    private readonly List<WorldEvent> _events = new();
    private readonly Dictionary<int, int> _lastSightCell = new();
    private readonly Queue<Command> _pendingCommands = new();
    private readonly Dictionary<int, PlayerState> _players = new();
    private readonly HashSet<int> _takenFortresses = new();
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

    public Island? FindIsland(int id) => _islands.Find(i => i.Id == id);

    /// <summary>Contracts on offer and cargo afloat. Empty until <see cref="Contracts.OpenMarkets"/>.</summary>
    public TradeBoard Trade { get; } = new();

    /// <summary>Shells in the air.</summary>
    public IReadOnlyList<AreaStrike> Strikes => _strikes;

    /// <summary>Lobs a shell from <paramref name="owner"/> that lands on <paramref name="target"/> after <paramref name="flightTicks"/>.</summary>
    public AreaStrike LaunchStrike(Ship owner, Vector2 target, float radius, float damage, int flightTicks, ClusterEffect? cluster = null,
        FireEffect? fire = null) =>
        LaunchStrike(owner.Id, owner.Team, owner.Position, target, radius, damage, flightTicks, cluster, fire);

    private AreaStrike LaunchStrike(int ownerShipId, Team team, Vector2 origin, Vector2 target, float radius, float damage, int flightTicks,
        ClusterEffect? cluster, FireEffect? fire = null)
    {
        var strike = new AreaStrike
        {
            Id = _nextEntityId++,
            OwnerShipId = ownerShipId,
            Team = team,
            Origin = origin,
            Target = target,
            Radius = radius,
            Damage = damage,
            LaunchTick = Tick,
            ImpactTick = Tick + Math.Max(1, flightTicks),
            Cluster = cluster,
            Fire = fire,
        };
        _strikes.Add(strike);
        Emit(new AreaStrikeLaunched(Tick, strike.Id, strike.OwnerShipId, strike.Team, strike.Origin, strike.Target, radius, damage, strike.ImpactTick));
        return strike;
    }

    /// <summary>Burning water (Firestorm): public, so anyone can keep out of it.</summary>
    public IReadOnlyList<FireZone> Fires => _fires;

    /// <summary>Adds a fire that's already started, for a client mirroring the server's.</summary>
    public void AddFire(FireZone fire) => _fires.Add(fire);

    /// <summary>Drops fires that have burned out by now, for a client mirroring the server's (which never steps).</summary>
    public void PutOutFires() => _fires.RemoveAll(f => f.EndTick <= Tick);

    /// <summary>A Hunter's Mark lasts this long after each hit.</summary>
    public const float MarkSeconds = 5f;
    public static readonly int MarkTicks = (int)(MarkSeconds * SimConstants.TickRate);

    /// <summary>A Chain Shot slow lasts this long after each hit.</summary>
    public const float SlowSeconds = 3f;
    public static readonly int SlowTicks = (int)(SlowSeconds * SimConstants.TickRate);
    public const string SlowSource = "slowed";

    /// <summary>A ram does its damage at most this often.</summary>
    public static readonly int RamCooldownTicks = SimConstants.TickRate;

    /// <summary>An echo fires this long after the shot it echoes.</summary>
    public const float EchoSeconds = 0.5f;
    public static readonly int EchoTicks = (int)(EchoSeconds * SimConstants.TickRate);

    /// <summary>A ricochet looks this far for its next target.</summary>
    public const float RicochetRange = 10f;

    /// <summary>
    /// The furthest back a lagging player's shots look for what they hit (see <see cref="Ship.ShotRewindTicks"/>): half
    /// a second. Beyond it, the shooter's view is too stale to favour over everyone else's.
    /// </summary>
    public const int MaxShotRewindTicks = SimConstants.TickRate / 2;

    /// <summary>
    /// Hurts <paramref name="target"/> on behalf of <paramref name="attackerShipId"/>: more if it's marked, and it
    /// becomes marked if the attacker carries Hunter's Mark. Every weapon's damage comes through here.
    /// </summary>
    public void DealDamage(Ship target, float amount, int attackerShipId)
    {
        if (target.MarkedUntilTick >= Tick)
            amount *= 1f + target.MarkBonus;
        target.Health = MathF.Max(0f, target.Health - amount);
        RecordHit(target, attackerShipId);
        if (FindShip(attackerShipId)?.PerkValue(Perk.HuntersMark) is > 0f and var mark)
        {
            target.MarkBonus = target.MarkedUntilTick >= Tick ? MathF.Max(target.MarkBonus, mark) : mark;
            target.MarkedUntilTick = Tick + MarkTicks;
        }
    }

    /// <summary>Slows <paramref name="ship"/> by <paramref name="fraction"/> for <see cref="SlowSeconds"/> (the stronger slow wins).</summary>
    public void Slow(Ship ship, float fraction)
    {
        var current = ship.SlowedUntilTick >= Tick
            ? ship.Modifiers.Where(m => m.Source == SlowSource).Select(m => 1f - m.Value).DefaultIfEmpty(0f).Max()
            : 0f;
        ship.RemoveModifiers(SlowSource);
        ship.AddModifier(new StatModifier(StatId.MaxSpeed, ModifierKind.Multiplier, 1f - MathF.Max(current, fraction), SlowSource));
        ship.SlowedUntilTick = Tick + SlowTicks;
    }

    /// <summary>Shots laid but not yet fired.</summary>
    public IReadOnlyList<ShotWarning> Warnings => _warnings;

    /// <summary>Adds a shot warning, for a client mirroring the server's.</summary>
    public void AddWarning(ShotWarning warning) => _warnings.Add(warning);

    /// <summary>Drops the warnings that have fired by <paramref name="tick"/>, or whose ship is gone, for a client mirroring the server's.</summary>
    public void RemoveSpentWarnings(double tick) => _warnings.RemoveAll(w => w.FireTick <= tick || FindShip(w.ShipId) is null);

    /// <summary>Adds an already-launched strike, for a client mirroring the server's shells.</summary>
    public void AddStrike(AreaStrike strike) => _strikes.Add(strike);

    public bool RemoveStrike(int id) => _strikes.RemoveAll(s => s.Id == id) > 0;

    public void AddIsland(Island island) => _islands.Add(island);

    /// <summary>Whether <paramref name="island"/> has been plundered. Each island can only be plundered once a run.</summary>
    public bool IsPlundered(Island island) => _plunderedIslands.Contains(island.Id);

    public void MarkPlundered(Island island) => _plunderedIslands.Add(island.Id);

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

    /// <summary>Runs fortress rewards and the bosses when set; null for worlds that only do what they're told (tests, sandboxes).</summary>
    public RunDirector? Director { get; set; }

    /// <summary>
    /// Whether players' shots hurt other players (PvP). Pirates never hurt each other either way, and no ship
    /// ever hurts itself.
    /// </summary>
    public bool FriendlyFire { get; set; }

    /// <summary>Whether a shot from <paramref name="attacker"/> (of <paramref name="attackerTeam"/>) can damage <paramref name="target"/>.</summary>
    public bool CanDamage(int attackerShipId, Team attackerTeam, Ship target) =>
        target.Id != attackerShipId
        && (target.Team != attackerTeam || (FriendlyFire && attackerTeam == Team.Players));

    /// <summary>
    /// True once every player was sunk at the same time, or the last boss was (see <see cref="IsVictory"/>). Nothing
    /// respawns and no more bosses come.
    /// </summary>
    public bool IsRunOver { get; private set; }

    /// <summary>The run ended with the last boss sunk.</summary>
    public bool IsVictory { get; private set; }

    public void EndRun(bool victory = false)
    {
        if (IsRunOver)
            return;
        IsRunOver = true;
        IsVictory = victory;
        foreach (var player in _players.Values)
        {
            player.RespawnTicksRemaining = 0;
            player.LostShip = null;
        }
        Emit(new RunEnded(Tick, victory));
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

    public bool RemoveProjectile(Projectile projectile) => _projectiles.Remove(projectile);

    /// <summary>Takes a player out of the run (they left): their ship goes and they stop counting toward raid size.</summary>
    public void RemovePlayer(int playerId)
    {
        if (GetPlayerShip(playerId) is { } ship)
        {
            RemoveShip(ship.Id);
            Emit(new ShipSunk(Tick, ship.Id, null));
            Contracts.SpillCargo(this, ship); // their cargo stays in play for everyone else
        }
        _players.Remove(playerId);
    }

    /// <summary>Replaces the set of plundered islands, for mirroring the server.</summary>
    public void SetPlunderedIslands(IEnumerable<int> islandIds)
    {
        _plunderedIslands.Clear();
        _plunderedIslands.UnionWith(islandIds);
    }

    public IReadOnlyCollection<int> PlunderedIslands => _plunderedIslands;

    /// <summary>Fortresses whose every gun has fallen. Taken islands stay taken for the rest of the run.</summary>
    public IReadOnlyCollection<int> TakenFortresses => _takenFortresses;

    /// <summary>
    /// Whether ships can trade at <paramref name="island"/>: shop for upgrades, weapons, skills and repairs, and buy
    /// contracts. Every shipyard, and every fortress once it's taken.
    /// </summary>
    public bool IsPort(Island island) => island.HasShipyard || (island.IsFortress && _takenFortresses.Contains(island.Id));

    /// <summary>Whether <paramref name="island"/> is a fortress still in pirate hands.</summary>
    public bool IsHeld(Island island) => island.IsFortress && !_takenFortresses.Contains(island.Id);

    /// <summary>Marks a fortress taken and announces it. False if it already was (or isn't a fortress).</summary>
    public bool TakeFortress(Island island)
    {
        if (!island.IsFortress || !_takenFortresses.Add(island.Id))
            return false;
        Emit(new FortressTaken(Tick, island.Id));
        return true;
    }

    /// <summary>Sets the tick counter, for a client mirroring the server's clock.</summary>
    public void SetTick(long tick) => Tick = tick;

    public Projectile SpawnProjectile(Ship owner, Vector2 position, Vector2 velocity, float damage, int lifetimeTicks,
        float radius = Projectile.DefaultRadius, ShotEffects? effects = null)
    {
        effects ??= ShotEffects.None;
        var projectile = new Projectile(_nextEntityId++, owner.Id, owner.Team, damage, radius)
        {
            IgnoredIslandId = owner.FortIslandId,
            Position = position,
            PreviousPosition = position,
            Velocity = velocity,
            RemainingTicks = lifetimeTicks,
            Origin = position,
            Effects = effects,
            PierceRemaining = effects.Pierce,
            RewindTicks = owner.ShotRewindTicks,
        };
        _projectiles.Add(projectile);
        Emit(new ProjectileSpawned(Tick, projectile.Id, owner.Id, owner.Team, position, velocity, damage, lifetimeTicks, radius));
        return projectile;
    }

    public Ship? GetPlayerShip(int playerId) => _ships.Find(s => s.OwnerPlayerId == playerId);

    /// <summary>Queues a command to be applied at the start of the next tick.</summary>
    public void Enqueue(Command command) => _pendingCommands.Enqueue(command);

    /// <summary>
    /// Everything stops while anyone has cards to choose (see <see cref="CardRewards"/>), for as long as it takes: the
    /// tick doesn't advance, so nothing moves, reloads, burns down, or comes due, and only choices and the helm get
    /// through. A player who leaves takes their offers with them.
    /// </summary>
    public bool IsPaused => !IsRunOver && _players.Values.Any(p => p.CardOffers.Count > 0 || p.NeedsStartingWeapon);

    public void Step()
    {
        if (IsPaused)
        {
            StepPaused();
            return;
        }

        const float dt = SimConstants.TickDelta;

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

        FireWarnedShots();
        FireEchoes();

        foreach (var ship in _ships)
        {
            if (!ship.IsFort)
                ShipMovement.Step(ship, dt, Wind);
        }

        _contacts.Clear();
        ShipMovement.ResolveCollisions(_ships, _contacts);
        foreach (var (a, b) in _contacts)
        {
            Ram(a, b);
            Ram(b, a);
        }

        foreach (var ship in _ships)
        {
            // Forts are built on land.
            if (!ship.IsFort && IslandCollision.Resolve(ship, _islands))
                Emit(new ShipGrounded(Tick, ship.Id));
        }

        RevealMap();

        foreach (var ship in _ships)
            ShipMovement.ClampToBounds(ship, new Vector2(-OutOfBoundsMargin), WorldSize + new Vector2(OutOfBoundsMargin));

        StepProjectiles(dt);
        StepStrikes();
        StepFires(dt);
        WearOff();
        Regenerate(dt);

        Plundering.Step(this);
        Contracts.Step(this);

        ResolveSinkings();
        Respawning.Step(this);

        Director?.Update(this);

        Tick++;
    }

    /// <summary>
    /// A step of real time while the game is paused for cards. The tick stays put. Commands still arrive: card choices
    /// and the helm (sail, rudder, course, anchor key, shopping) are taken, guns aren't.
    /// </summary>
    private void StepPaused()
    {
        // Hold everything exactly where it is, with nothing left to blend between.
        foreach (var ship in _ships)
        {
            ship.PreviousPosition = ship.Position;
            ship.PreviousHeading = ship.Heading;
        }
        foreach (var projectile in _projectiles)
            projectile.PreviousPosition = projectile.Position;

        while (_pendingCommands.TryDequeue(out var command))
        {
            if (command is CastAbilityCommand)
                Emit(new CommandRejected(Tick, command.PlayerId, command, RejectionReason.Paused));
            else
                Apply(command);
        }
    }

    /// <summary>Notes who just hit <paramref name="victim"/>: the last hitter takes the kill, and every player shares in it.</summary>
    private void RecordHit(Ship victim, int attackerShipId)
    {
        victim.LastHitByShipId = attackerShipId;
        victim.LastHitTick = Tick;
        if (FindShip(attackerShipId)?.OwnerPlayerId is { } playerId)
            victim.RecordPlayerHit(playerId, Tick);
    }

    private void ResolveSinkings()
    {
        // Second Wind: a killing blow leaves the ship afloat instead, while it's ready.
        foreach (var ship in _ships)
        {
            if (!ship.IsSunk || ship.PerkValue(Perk.SecondWindHeal) is not (> 0f and var heal) || Tick < ship.SecondWindReadyTick)
                continue;
            ship.Health = heal * ship.Stats.MaxHealth;
            ship.SecondWindReadyTick = Tick + (long)(ship.PerkValue(Perk.SecondWindCooldown) * SimConstants.TickRate);
        }

        foreach (var victim in _ships)
        {
            if (!victim.IsSunk || victim.LastHitByShipId is not { } killerId)
                continue;

            // Credit the kill even if the killer went down in the same exchange.
            var killer = _ships.Find(s => s.Id == killerId);
            if (killer is not null && CanDamage(killer.Id, killer.Team, victim))
                KillRewards.Grant(this, killer, victim);
        }

        foreach (var victim in _ships)
        {
            if (!victim.IsSunk)
                continue;
            Emit(new ShipSunk(Tick, victim.Id, victim.LastHitByShipId));
            Contracts.SpillCargo(this, victim);
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

    /// <summary>Every ship still afloat mends at its <see cref="ShipStats.HealthRegen"/> rate, up to its maximum.</summary>
    private void Regenerate(float dt)
    {
        foreach (var ship in _ships)
        {
            if (!ship.IsSunk)
                ship.Health = MathF.Min(ship.Stats.MaxHealth, ship.Health + ship.Stats.HealthRegen * dt);
        }
    }

    /// <summary>A ship with a ram ran into <paramref name="target"/>: damage, at most once a second.</summary>
    private void Ram(Ship rammer, Ship target)
    {
        if (rammer.PerkValue(Perk.RamDamage) is not (> 0f and var damage) || Tick < rammer.RamReadyTick
            || rammer.IsSunk || !CanDamage(rammer.Id, rammer.Team, target))
            return;
        DealDamage(target, damage, rammer.Id);
        rammer.RamReadyTick = Tick + RamCooldownTicks;
        Emit(new ShipRammed(Tick, rammer.Id, target.Id));
    }

    /// <summary>Queues a weapon's echo, if the ship carries Echo: it fires again shortly, at a share of its damage.</summary>
    private void ScheduleEcho(Ship ship, AbilitySlot slot, Vector2 target)
    {
        if (ship.PerkValue(Perk.Echo) is > 0f and var scale)
            _echoes.Add(new PendingEcho(ship.Id, slot, target, Tick + EchoTicks, scale));
    }

    /// <summary>Fires the echoes that are due, from wherever their ships are now; a sunk ship's are lost.</summary>
    private void FireEchoes()
    {
        if (_echoes.Count == 0)
            return;
        foreach (var echo in _echoes.Where(e => e.DueTick <= Tick).ToList())
        {
            if (FindShip(echo.ShipId) is not { IsSunk: false } ship || ship.GetAbility(echo.Slot) is not { } ability)
                continue;
            ship.CastDamageScale = echo.Scale;
            ship.IsEchoing = true;
            ability.Definition.Cast(this, ship, echo.Target);
            ship.CastDamageScale = 1f;
            ship.IsEchoing = false;
        }
        _echoes.RemoveAll(e => e.DueTick <= Tick);
    }

    /// <summary>Burns every ship in a fire for this tick, and puts out the fires that are done.</summary>
    private void StepFires(float dt)
    {
        if (_fires.Count == 0)
            return;
        Span<Vector2> hull = stackalloc Vector2[HullShape.PointCount];
        foreach (var fire in _fires)
        {
            foreach (var ship in _ships)
            {
                if (ship.IsSunk || !CanDamage(fire.OwnerShipId, fire.Team, ship))
                    continue;
                HullShape.GetWorldOutline(ship.Position, ship.Heading, ship.Stats, hull);
                if (Geometry.DistanceToConvex(hull, fire.Position) <= fire.Radius)
                    DealDamage(ship, fire.Dps * dt, fire.OwnerShipId);
            }
        }
        _fires.RemoveAll(f => f.EndTick <= Tick);
    }

    /// <summary>Marks and slows run out.</summary>
    private void WearOff()
    {
        foreach (var ship in _ships)
        {
            ship.IsMarked = ship.MarkedUntilTick >= Tick;
            if (ship.SlowedUntilTick >= 0 && Tick >= ship.SlowedUntilTick)
            {
                ship.RemoveModifiers(SlowSource);
                ship.SlowedUntilTick = -1;
            }
        }
    }

    /// <summary>Fires every laid shot that's due, from wherever its ship is now; a sunk ship's shots are lost.</summary>
    private void FireWarnedShots()
    {
        if (_warnings.Count == 0)
            return;
        foreach (var warning in _warnings.ToList())
        {
            if (warning.FireTick > Tick)
                continue;
            if (FindShip(warning.ShipId) is { IsSunk: false } ship && ship.GetAbility(warning.Slot) is { } ability
                && ability.Definition.CastWarned(this, ship, warning))
                Emit(new AbilityCast(Tick, ship.Id, warning.Slot, ability.DurationTicks(warning.Channel), warning.Channel));
        }
        _warnings.RemoveAll(w => w.FireTick <= Tick);
    }

    /// <summary>Bursts every shell that's due: hurts each hostile hull within its blast radius, and scatters any bomblets.</summary>
    private void StepStrikes()
    {
        Span<Vector2> hull = stackalloc Vector2[HullShape.PointCount];
        foreach (var strike in _strikes.ToList()) // bomblets join the list as we go
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
                DealDamage(ship, strike.Damage, strike.OwnerShipId);
            }
            Emit(new AreaStrikeImpact(Tick, strike.Id, strike.Target, strike.Radius));

            if (strike.Fire is { } burn)
            {
                var fire = new FireZone
                {
                    Id = _nextEntityId++, OwnerShipId = strike.OwnerShipId, Team = strike.Team, Position = strike.Target,
                    Radius = strike.Radius, Dps = burn.Dps, StartTick = Tick, EndTick = Tick + burn.Ticks,
                };
                _fires.Add(fire);
                Emit(new FireStarted(Tick, fire.Id, fire.OwnerShipId, fire.Team, fire.Position, fire.Radius, fire.Dps, fire.EndTick));
            }

            if (strike.Cluster is { Count: > 0 } cluster)
            {
                // Evenly round a ring, turned by the shell's id so salvos don't all scatter the same way.
                var turn = strike.Id * 0.7f;
                for (var i = 0; i < cluster.Count; i++)
                {
                    var angle = turn + MathF.Tau * i / cluster.Count;
                    var point = strike.Target + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * cluster.Spread;
                    LaunchStrike(strike.OwnerShipId, strike.Team, strike.Target, point, cluster.Radius,
                        strike.Damage * cluster.DamageFraction, cluster.DelayTicks, cluster: null);
                }
            }
        }
        _strikes.RemoveAll(s => s.ImpactTick <= Tick);
    }

    private void StepProjectiles(float dt)
    {
        List<(Projectile Shot, Ship Struck)>? bounces = null;
        // Every ship has moved for this tick: remember where, for shots that look back (see Projectile.RewindTicks).
        foreach (var ship in _ships)
            ship.RecordPose(Tick + 1);

        foreach (var projectile in _projectiles)
        {
            var from = projectile.Position;
            projectile.Position += projectile.Velocity * dt;
            projectile.RemainingTicks--;

            // A shot that reaches land stops there, though it can still strike a fort standing on the shore, and the
            // shore round a fort doesn't stop it: it flies on over the beach to the walls.
            var hitsLand = !projectile.Effects.IgnoresLand
                           && LineHitsLand(from, projectile.Position, projectile.Radius, projectile.IgnoredIslandId, clearForts: true);

            foreach (var ship in _ships)
            {
                if ((hitsLand && !ship.IsFort) || ship.IsSunk || projectile.HasHit(ship.Id)
                    || !CanDamage(projectile.OwnerShipId, projectile.Team, ship))
                    continue;
                var (hullAt, hullHeading) = projectile.RewindTicks > 0
                    ? ship.PoseAt(Tick + 1 - projectile.RewindTicks)
                    : (ship.Position, ship.Heading);
                if (!HullShape.SegmentHits(ship, hullAt, hullHeading, from, projectile.Position, projectile.Radius))
                    continue;

                var effects = projectile.Effects;
                var distance = Vector2.Distance(projectile.Origin, projectile.Position);
                DealDamage(ship, projectile.Damage * effects.DamageMultiplier(distance), projectile.OwnerShipId);
                projectile.RecordHit(ship.Id);
                if (effects.SlowOnHit > 0f)
                    Slow(ship, effects.SlowOnHit);

                if (effects.LongRangeRefund > 0f && effects.IsLongRange(distance) && effects.AbilityId is { } abilityId)
                    FindShip(projectile.OwnerShipId)?.FindAbility(abilityId)?.Refund(effects.LongRangeRefund);

                var passesThrough = projectile.PierceRemaining > 0;
                Emit(new ProjectileImpact(Tick, projectile.Id, ship.Id, passesThrough));
                if (passesThrough)
                {
                    projectile.PierceRemaining--;
                    continue;
                }
                projectile.RemainingTicks = 0;
                if (effects.Ricochets > 0)
                    (bounces ??= new()).Add((projectile, ship));
                break;
            }

            if (hitsLand && projectile.RemainingTicks > 0)
            {
                projectile.RemainingTicks = 0;
                Emit(new ProjectileImpact(Tick, projectile.Id, null));
            }
        }

        _projectiles.RemoveAll(p => p.RemainingTicks <= 0);
        foreach (var (shot, struck) in bounces ?? Enumerable.Empty<(Projectile, Ship)>())
            Ricochet(shot, struck);
    }

    /// <summary>A ricocheting shot bounces from <paramref name="struck"/> on toward the nearest other enemy in reach, if any.</summary>
    private void Ricochet(Projectile shot, Ship struck)
    {
        if (FindShip(shot.OwnerShipId) is not { } owner)
            return;
        Ship? next = null;
        var nearest = RicochetRange * RicochetRange;
        foreach (var ship in _ships)
        {
            if (ship == struck || ship.IsSunk || !CanDamage(shot.OwnerShipId, shot.Team, ship))
                continue;
            var d = Vector2.DistanceSquared(ship.Position, shot.Position);
            if (d <= nearest)
            {
                next = ship;
                nearest = d;
            }
        }
        if (next is null)
            return;
        var speed = shot.Velocity.Length();
        var direction = Vector2.Normalize(next.Position - shot.Position);
        var lifetime = (int)MathF.Ceiling((RicochetRange + 2f) / MathF.Max(speed, 1f) * SimConstants.TickRate);
        var bounce = SpawnProjectile(owner, shot.Position, direction * speed, shot.Damage, lifetime, shot.Radius,
            shot.Effects with { Ricochets = shot.Effects.Ricochets - 1 });
        bounce.RecordHit(struck.Id);
    }

    /// <summary>
    /// Whether a ball of <paramref name="radius"/> travelling from <paramref name="from"/> to <paramref name="to"/>
    /// would strike land, other than <paramref name="ignoredIslandId"/> (a fort firing over its own island). With
    /// <paramref name="clearForts"/>, land within <see cref="FortClearance"/> of a fort standing on it doesn't count.
    /// </summary>
    public bool LineHitsLand(Vector2 from, Vector2 to, float radius, int? ignoredIslandId = null, bool clearForts = false)
    {
        foreach (var island in _islands)
        {
            if (island.Id == ignoredIslandId)
                continue;
            if (Geometry.DistanceToSegment(island.Center, from, to) > island.BoundingRadius + radius)
                continue;
            if (!Geometry.SegmentTouchesConvex(island.Outline, from, to, radius))
                continue;
            if (clearForts && IsHeld(island) && NearStandingFort(island, from, to))
                continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// How near a fort the shore stops blocking shots. Forts stand on curving coasts, and a shot aimed straight at one
    /// from an angle would otherwise clip the beach beside it, short of walls the player can plainly see.
    /// </summary>
    public const float FortClearance = 3f;

    private bool NearStandingFort(Island island, Vector2 from, Vector2 to)
    {
        foreach (var ship in _ships)
        {
            if (ship.FortIslandId == island.Id && !ship.IsSunk
                && Geometry.DistanceToSegment(ship.Position, from, to) <= FortClearance)
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
        // Cards belong to the player, not the ship: they can be chosen (or rerolled) while waiting to respawn.
        if (command is ChooseCardCommand choose)
            return CardRewards.TryChoose(this, command.PlayerId, choose.CardId);
        if (command is RerollCardsCommand)
            return CardRewards.TryReroll(this, command.PlayerId);
        if (command is ChooseStartingWeaponCommand weapon)
            return Runs.TryChooseStartingWeapon(this, command.PlayerId, weapon.AbilityId);

        // Commands only ever act on the issuing player's own ship; this is also the server-side ownership check.
        var ship = GetPlayerShip(command.PlayerId);
        if (ship is null)
            return RejectionReason.NoShip;

        switch (command)
        {
            case MoveCommand or AdjustThrottleCommand when ship.IsAnchored:
                return RejectionReason.Anchored; // held fast: no sailing anywhere until the anchor is up
            case MoveCommand move:
                ship.MoveTarget = Vector2.Clamp(move.Target, Vector2.Zero, WorldSize);
                ship.IsHoldingCourse = false;
                ship.Rudder = 0;
                if (ship.Throttle <= 0)
                    ship.Throttle = ShipMovement.AutopilotThrottle;
                return null;
            case StopCommand:
                ship.MoveTarget = null;
                ship.IsHoldingCourse = false;
                ship.Throttle = 0;
                return null;
            case AdjustThrottleCommand adjust:
                ship.Throttle = Math.Clamp(ship.Throttle + adjust.Delta, ShipMovement.AsternThrottle, ShipMovement.ThrottleLevels);
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
            case PurchaseRepairCommand:
                return Shipyards.TryRepair(this, ship);
            case PurchaseUpgradeCommand purchase:
                return Shipyards.ToRejection(Shipyards.TryPurchase(this, ship, purchase.UpgradeId));
            case UnlockAbilityCommand unlock:
                return Shipyards.TryUnlockAbility(this, ship, unlock.AbilityId);
            case PurchaseSkillCommand skill:
                return Shipyards.TryPurchaseSkill(this, ship, skill.SkillId);
            case PurchaseContractCommand contract:
                return Contracts.TryPurchase(this, ship, contract.ContractId);
            case SetRudderCommand rudder:
                ship.Rudder = Math.Clamp(rudder.Rudder, -1, 1);
                if (ship.Rudder != 0)
                {
                    ship.MoveTarget = null;
                    ship.IsHoldingCourse = false;
                }
                return null;
            case CastAbilityCommand cast:
                ship.ShotRewindTicks = cast.ViewTick is { } seen ? (int)Math.Clamp(Tick - seen, 0, MaxShotRewindTicks) : 0;
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

        var windup = ability.Definition.WindupTicksFor(ship);
        if (windup > 0)
        {
            // Lay the gun now and fire later: the reload starts now, so it can't be laid twice.
            var warning = new ShotWarning
            {
                ShipId = ship.Id, Slot = slot, Channel = channel, Target = target, StartTick = Tick, FireTick = Tick + windup,
            };
            _warnings.Add(warning);
            ability.StartCooldown(channel, ability.Definition.CooldownTicksFor(ship), ship.Stats.CooldownSpeed);
            Emit(new ShotWarned(Tick, ship.Id, slot, target, warning.FireTick, channel));
            return null; // AbilityCast goes out when it fires
        }

        if (!ability.Definition.Cast(this, ship, target))
            return RejectionReason.CastFailed;

        ability.StartCooldown(channel, ability.Definition.CooldownTicksFor(ship), ship.Stats.CooldownSpeed);
        Emit(new AbilityCast(Tick, ship.Id, slot, ability.DurationTicks(channel), channel));
        ScheduleEcho(ship, slot, target);
        return null;
    }

}
