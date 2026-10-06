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


    /// <summary>
    /// When a pirate ship sinks, the pirates within this range are roused: a stack of <see cref="StatusId.Frenzy"/>,
    /// this strong, each.
    /// </summary>
    public const float RallyRange = 12f;
    public const float RallyPower = 0.06f;

    /// <summary>A ram does its damage at most this often.</summary>
    public static readonly int RamCooldownTicks = SimConstants.TickRate;

    /// <summary>A Ram card's damage is quoted at a stock sloop's top speed, and scales with the rammer's speed from there.</summary>
    public static readonly float RamReferenceSpeed = ShipStats.Sloop.MaxSpeed;

    /// <summary>What a ram quoted at <paramref name="quoted"/> damage does at <paramref name="speed"/>: twice as fast, twice the damage.</summary>
    public static float RamDamageAt(float quoted, float speed) => quoted * MathF.Abs(speed) / RamReferenceSpeed;

    /// <summary>An echo fires this long after the shot it echoes.</summary>
    public const float EchoSeconds = 0.5f;
    public static readonly int EchoTicks = (int)(EchoSeconds * SimConstants.TickRate);

    /// <summary>A ricochet (or a fork) looks this far for its next target.</summary>
    public const float RicochetRange = 10f;

    /// <summary>
    /// However many hits give back its reload (Hot Guns), a weapon fires at most this often: it keeps a deck from
    /// going off every tick, for the players and the network both.
    /// </summary>
    public const float HitRefundFloorSeconds = 0.5f;
    public static readonly int HitRefundFloorTicks = (int)(HitRefundFloorSeconds * SimConstants.TickRate);

    /// <summary>The burning water a broadside hit leaves (Incendiary).</summary>
    public const float HitFireRadius = 1.2f;

    /// <summary>A burning wake is patches of fire this far apart, each this wide (Burning Wake).</summary>
    public const float WakeSpacing = 1.5f;
    public const float WakeRadius = 1f;

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
        if (target.FindStatus(StatusId.Marked) is { } mark)
            amount *= 1f + mark.Power;
        target.Health = MathF.Max(0f, target.Health - amount);
        RecordHit(target, attackerShipId);
        if (FindShip(attackerShipId)?.PerkValue(Perk.HuntersMark) is > 0f and var marking)
            ApplyStatus(target, StatusId.Marked, marking, attackerShipId);
    }

    /// <summary>
    /// A weapon of <paramref name="attackerShipId"/>'s struck <paramref name="target"/> (a shot, a shell's blast, a
    /// ram; not a fire or a burn ticking on): what its cards make a hit do besides damage.
    /// </summary>
    private void OnWeaponHit(int attackerShipId, Ship target)
    {
        if (FindShip(attackerShipId)?.PerkValue(Perk.BurnOnHit) is > 0f and var burn)
            ApplyStatus(target, StatusId.Burning, burn, attackerShipId);
    }

    /// <summary>
    /// Puts a status on <paramref name="ship"/>, or adds to the one it has: <paramref name="stacks"/> more (up to its
    /// most), at the stronger of the two powers, lasting its full time again from now. Credited to
    /// <paramref name="sourceShipId"/>. Any ship can carry any status, player's or pirate's.
    /// </summary>
    public void ApplyStatus(Ship ship, StatusId id, float power, int sourceShipId, int stacks = 1)
    {
        if (ship.IsSunk || power <= 0f || stacks <= 0)
            return;
        var definition = Statuses.Get(id);
        var status = ship.FindStatus(id);
        if (status is null)
        {
            status = new StatusEffect { Id = id };
            ship.AddStatus(status);
        }
        var before = (status.Stacks, status.Power);
        status.Stacks = Math.Min(definition.MaxStacks, status.Stacks + stacks);
        status.Power = MathF.Max(status.Power, power);
        status.UntilTick = Tick + definition.Ticks;
        status.SourceShipId = sourceShipId;
        if (before != (status.Stacks, status.Power))
            RefreshStatusModifiers(ship, status);
    }

    /// <summary>Takes a status off <paramref name="ship"/>, and whatever it was doing to its stats.</summary>
    public void RemoveStatus(Ship ship, StatusId id)
    {
        if (ship.FindStatus(id) is not { } status)
            return;
        ship.RemoveStatus(status);
        ship.RemoveModifiers(Statuses.ModifierSource(id));
    }

    private static void RefreshStatusModifiers(Ship ship, StatusEffect status)
    {
        var modifiers = Statuses.ModifiersFor(status.Id, status.Stacks, status.Power).ToList();
        if (modifiers.Count == 0)
            return;
        ship.RemoveModifiers(Statuses.ModifierSource(status.Id));
        foreach (var modifier in modifiers)
            ship.AddModifier(modifier);
    }

    /// <summary>Statuses do their work for this tick (burns burn), and the ones whose time is up come off.</summary>
    private void StepStatuses(float dt)
    {
        foreach (var ship in _ships)
        {
            if (ship.IsSunk || ship.Statuses.Count == 0)
                continue;
            for (var i = ship.Statuses.Count - 1; i >= 0; i--)
            {
                var status = ship.Statuses[i];
                if (Tick >= status.UntilTick)
                {
                    RemoveStatus(ship, status.Id);
                    continue;
                }
                if (status.Id == StatusId.Burning)
                    DealDamage(ship, status.Power * status.Stacks * dt, status.SourceShipId);
            }
        }
    }

    /// <summary>
    /// <paramref name="killer"/> sank <paramref name="victim"/>: a Frenzy for a killer that has it, and every pirate
    /// near a sunk pirate is roused (see <see cref="RallyRange"/>).
    /// </summary>
    private void OnKill(Ship killer, Ship victim)
    {
        if (killer.PerkValue(Perk.Frenzy) is > 0f and var frenzy)
            ApplyStatus(killer, StatusId.Frenzy, frenzy, killer.Id);
        if (victim.Team != Team.Pirates)
            return;
        foreach (var ship in _ships)
        {
            if (ship != victim && ship.Team == Team.Pirates && !ship.IsSunk
                && Vector2.DistanceSquared(ship.Position, victim.Position) <= RallyRange * RallyRange)
                ApplyStatus(ship, StatusId.Frenzy, RallyPower, victim.Id);
        }
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

        FireBroadsidesByThemselves();
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
        StepStatuses(dt);
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
            {
                KillRewards.Grant(this, killer, victim);
                OnKill(killer, victim);
            }
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

    /// <summary>
    /// A ship with a ram ran into <paramref name="target"/>: damage by how fast it was going, at most once a second. A
    /// nudge too slow to do a point of damage doesn't count, so it doesn't spend the ram.
    /// </summary>
    private void Ram(Ship rammer, Ship target)
    {
        if (rammer.PerkValue(Perk.RamDamage) is not (> 0f and var quoted) || Tick < rammer.RamReadyTick
            || rammer.IsSunk || !CanDamage(rammer.Id, rammer.Team, target))
            return;
        var damage = RamDamageAt(quoted, rammer.Speed);
        if (damage < 1f)
            return;
        DealDamage(target, damage, rammer.Id);
        OnWeaponHit(rammer.Id, target);
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

    /// <summary>
    /// Sets the water burning at <paramref name="position"/>, hurting <paramref name="ownerShipId"/>'s enemies in it.
    /// With <paramref name="unlessBurning"/>, not where that ship's fire already burns, with half its time or more
    /// left: hits and wakes would otherwise pile hundreds of fires on the same few tiles.
    /// </summary>
    private void StartFire(int ownerShipId, Team team, Vector2 position, float radius, FireEffect burn, bool unlessBurning = false)
    {
        if (unlessBurning)
        {
            foreach (var burning in _fires)
            {
                if (burning.OwnerShipId == ownerShipId && burning.EndTick - Tick >= burn.Ticks / 2
                    && Vector2.DistanceSquared(burning.Position, position) <= burning.Radius * burning.Radius * 0.25f)
                    return;
            }
        }
        var fire = new FireZone
        {
            Id = _nextEntityId++, OwnerShipId = ownerShipId, Team = team, Position = position,
            Radius = radius, Dps = burn.Dps, StartTick = Tick, EndTick = Tick + burn.Ticks,
        };
        _fires.Add(fire);
        Emit(new FireStarted(Tick, fire.Id, fire.OwnerShipId, fire.Team, fire.Position, fire.Radius, fire.Dps, fire.EndTick));
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
                // Cheap reject first: there can be hundreds of fires burning at once.
                if (ship.IsSunk || Vector2.Distance(ship.Position, fire.Position) > fire.Radius + ship.Stats.Length / 2f
                    || !CanDamage(fire.OwnerShipId, fire.Team, ship))
                    continue;
                HullShape.GetWorldOutline(ship.Position, ship.Heading, ship.Stats, hull);
                if (Geometry.DistanceToConvex(hull, fire.Position) <= fire.Radius)
                    DealDamage(ship, fire.Dps * dt, fire.OwnerShipId);
            }
        }
        _fires.RemoveAll(f => f.EndTick <= Tick);
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
                OnWeaponHit(strike.OwnerShipId, ship);
            }
            Emit(new AreaStrikeImpact(Tick, strike.Id, strike.Target, strike.Radius));

            if (strike.Fire is { } burn)
                StartFire(strike.OwnerShipId, strike.Team, strike.Target, strike.Radius, burn);

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
        List<(Projectile Shot, Ship? Struck)>? skips = null;
        // Every ship has moved for this tick: remember where, for shots that look back (see Projectile.RewindTicks).
        foreach (var ship in _ships)
            ship.RecordPose(Tick + 1);

        foreach (var projectile in _projectiles)
        {
            var from = projectile.Position;
            projectile.Position += projectile.Velocity * dt;
            projectile.RemainingTicks--;
            var effects = projectile.Effects;
            if (effects.Wake is { } wake)
                LeaveWake(projectile, from, wake);

            // A shot that reaches land stops there, though it can still strike a fort standing on the shore, and the
            // shore round a fort doesn't stop it: it flies on over the beach to the walls.
            var hitsLand = !effects.IgnoresLand
                           && LineHitsLand(from, projectile.Position, projectile.Radius, projectile.IgnoredIslandId, clearForts: true);

            var stopped = false;
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

                var distance = Vector2.Distance(projectile.Origin, projectile.Position);
                DealDamage(ship, projectile.Damage * effects.DamageMultiplier(distance), projectile.OwnerShipId);
                projectile.RecordHit(ship.Id);
                OnShotHit(projectile, ship, distance);

                // Its first hit sends bounces on, even if it flies on through.
                if (effects.Bounces && !projectile.HasBounced)
                {
                    projectile.HasBounced = true;
                    (bounces ??= new()).Add((projectile, ship));
                }

                var passesThrough = projectile.PierceRemaining > 0;
                Emit(new ProjectileImpact(Tick, projectile.Id, ship.Id, passesThrough));
                if (passesThrough)
                {
                    projectile.PierceRemaining--;
                    continue;
                }
                projectile.RemainingTicks = 0;
                stopped = true;
                if (effects.Skips > 0)
                    (skips ??= new()).Add((projectile, ship));
                break;
            }

            if (hitsLand && projectile.RemainingTicks > 0)
            {
                projectile.RemainingTicks = 0;
                Emit(new ProjectileImpact(Tick, projectile.Id, null));
            }
            else if (!stopped && !hitsLand && projectile.RemainingTicks <= 0 && effects.Skips > 0)
                (skips ??= new()).Add((projectile, null)); // off the water at the end of its flight
        }

        _projectiles.RemoveAll(p => p.RemainingTicks <= 0);
        foreach (var (shot, struck) in bounces ?? Enumerable.Empty<(Projectile, Ship)>())
            Bounce(shot, struck);
        foreach (var (shot, struck) in skips ?? Enumerable.Empty<(Projectile, Ship?)>())
            Skip(shot, struck);
    }

    /// <summary>What a shot does to <paramref name="struck"/> besides its damage: slows, reloads, fires and bursts.</summary>
    private void OnShotHit(Projectile shot, Ship struck, float distance)
    {
        var effects = shot.Effects;
        OnWeaponHit(shot.OwnerShipId, struck);
        if (effects.SlowOnHit > 0f)
            ApplyStatus(struck, StatusId.Slowed, effects.SlowOnHit, shot.OwnerShipId);

        if (effects.AbilityId is { } abilityId && FindShip(shot.OwnerShipId)?.FindAbility(abilityId) is { } weapon)
        {
            if (effects.LongRangeRefund > 0f && effects.IsLongRange(distance))
                weapon.Refund(effects.LongRangeRefund);
            if (effects.KillRefund > 0f && struck.Health <= 0f)
                weapon.Refund(effects.KillRefund);
            if (effects.HitRefund > 0f)
                weapon.Refund(effects.HitRefund, HitRefundFloorTicks);
        }

        if (effects.HitFire is { } fire)
            StartFire(shot.OwnerShipId, shot.Team, shot.Position, HitFireRadius, fire, unlessBurning: true);
        if (effects.ExplosionRadius > 0f)
            LaunchStrike(shot.OwnerShipId, shot.Team, shot.Position, shot.Position, effects.ExplosionRadius,
                shot.Damage * effects.ExplosionDamage, 1, cluster: null);
    }

    /// <summary>Drops patches of burning water every <see cref="WakeSpacing"/> along the shot's path this tick (not on land).</summary>
    private void LeaveWake(Projectile shot, Vector2 from, FireEffect wake)
    {
        var step = shot.Position - from;
        var length = step.Length();
        if (length <= 0f)
            return;
        var along = WakeSpacing - shot.WakeTravelled; // from here to the next patch
        for (; along <= length; along += WakeSpacing)
        {
            var point = from + step * (along / length);
            if (DistanceToLand(point) > 0f)
                StartFire(shot.OwnerShipId, shot.Team, point, WakeRadius, wake, unlessBurning: true);
        }
        shot.WakeTravelled = length - (along - WakeSpacing);
    }

    /// <summary>
    /// A shot that struck <paramref name="struck"/> bounces on toward the nearest enemy in reach it hasn't already been
    /// at (Ricochet), or splits in two toward the two nearest (Fork). Bounces never pierce.
    /// </summary>
    private void Bounce(Projectile shot, Ship struck)
    {
        if (FindShip(shot.OwnerShipId) is not { } owner)
            return;
        var effects = shot.Effects;
        var splits = effects.Forks > 0;
        var lineage = shot.Lineage ??= new HashSet<int>();
        lineage.Add(struck.Id);
        var targets = _ships
            .Where(s => !s.IsSunk && !lineage.Contains(s.Id) && CanDamage(shot.OwnerShipId, shot.Team, s)
                        && Vector2.DistanceSquared(s.Position, shot.Position) <= RicochetRange * RicochetRange)
            .OrderBy(s => Vector2.DistanceSquared(s.Position, shot.Position))
            .Take(splits ? 2 : 1)
            .ToList();
        var next = splits ? effects with { Forks = effects.Forks - 1, Pierce = 0 } : effects with { Ricochets = effects.Ricochets - 1, Pierce = 0 };
        var speed = shot.Velocity.Length();
        var lifetime = (int)MathF.Ceiling((RicochetRange + 2f) / MathF.Max(speed, 1f) * SimConstants.TickRate);
        foreach (var target in targets)
        {
            lineage.Add(target.Id); // so its sister and cousins look elsewhere
            var direction = Vector2.Normalize(target.Position - shot.Position);
            var bounce = SpawnProjectile(owner, shot.Position, direction * speed, shot.Damage, lifetime, shot.Radius, next);
            bounce.RecordHit(struck.Id);
            bounce.Lineage = lineage;
        }
    }

    /// <summary>A ball skips on in the same direction, off the water or off <paramref name="struck"/> (Skip Shot).</summary>
    private void Skip(Projectile shot, Ship? struck)
    {
        if (FindShip(shot.OwnerShipId) is not { } owner)
            return;
        var skip = SpawnProjectile(owner, shot.Position, shot.Velocity, shot.Damage, shot.Effects.SkipTicks, shot.Radius,
            shot.Effects with { Skips = shot.Effects.Skips - 1 });
        if (struck is not null)
            skip.RecordHit(struck.Id);
        skip.HasBounced = shot.HasBounced;
        skip.Lineage = shot.Lineage;
    }

    /// <summary>
    /// Broadsides that fire themselves: a Man o' War's ring goes off whenever it's loaded and an enemy is in range,
    /// and with gun captains, each deck fires whenever it's loaded and an enemy is in its lane (the nearest, if several).
    /// </summary>
    private void FireBroadsidesByThemselves()
    {
        foreach (var ship in _ships)
        {
            if (ship.IsSunk)
                continue;
            for (var slot = AbilitySlot.One; slot <= AbilitySlot.Four; slot++)
            {
                if (ship.GetAbility(slot) is not { Definition: BroadsideVolley } guns)
                    continue;
                if (BroadsideVolley.FiresRing(ship))
                {
                    var range = BroadsideVolley.RangeFor(ship);
                    if (guns.IsChannelReady(BroadsideVolley.PortChannel)
                        && NearestEnemy(ship, other => Vector2.Distance(ship.Position, other.Position) <= range + other.Stats.Length / 2f) is { } enemy)
                        CastAbility(ship, slot, enemy.Position);
                }
                else if (BroadsideVolley.FiresItself(ship))
                {
                    foreach (var side in new[] { BroadsideSide.Port, BroadsideSide.Starboard })
                    {
                        if (guns.IsChannelReady(BroadsideVolley.ChannelOf(side))
                            && NearestEnemy(ship, other => BroadsideVolley.Covers(ship, side, other.Position, other.Stats.Radius)) is { } enemy)
                            CastAbility(ship, slot, enemy.Position);
                    }
                }
            }
        }
    }

    /// <summary>The nearest ship on another team that's afloat and <paramref name="within"/>, if any. Never a crewmate.</summary>
    private Ship? NearestEnemy(Ship ship, Func<Ship, bool> within)
    {
        Ship? nearest = null;
        var best = float.PositiveInfinity;
        foreach (var other in _ships)
        {
            if (other.IsSunk || other.Team == ship.Team)
                continue;
            var d = Vector2.DistanceSquared(ship.Position, other.Position);
            if (d < best && within(other))
            {
                nearest = other;
                best = d;
            }
        }
        return nearest;
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
        if (command is RerollCardsCommand reroll)
            return CardRewards.TryReroll(this, command.PlayerId, reroll.Tier);
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
