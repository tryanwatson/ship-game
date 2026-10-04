using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Progression;

/// <summary>What's coming, for the HUD: computed by the server's director each tick and mirrored by clients.</summary>
/// <param name="StormY">World Y of the storm's leading edge: everything south of it (greater Y) is in the storm.</param>
/// <param name="TicksUntilStorm">Countdown until the storm starts moving north; 0 once it has.</param>
/// <param name="Hunters">Bounty hunters afloat in the storm.</param>
public readonly record struct RunStatus(float StormY, int TicksUntilStorm, int Hunters);

/// <summary>
/// Keeps the crew moving north. A storm front rolls up the map from the southern edge, stopping short of the last
/// sea. The storm itself does no harm, but bounty hunters ride in it: a player inside it draws a hunter after
/// <see cref="FirstHunterSeconds"/>, and another every <see cref="HunterIntervalSeconds"/> they stay, up to
/// <see cref="MaxHuntersPerPlayer"/> each. Hunters are a level above the sea they appear in (a strong foe for those
/// waters, not an impossible one), so a quick dip into the storm is survivable and lingering in it isn't. They only
/// chase ships in the storm, and melt back into it if they leave it or have no one to chase for a while. Runs inside
/// <see cref="World.Step"/> on the server and draws from its own seeded RNG, so a run is reproducible from its seed.
/// The pirates already at sea are placed by <see cref="PirateCamps"/>.
/// </summary>
public sealed class RunDirector
{
    /// <summary>The storm waits this long before it starts north, so the crew can get its bearings.</summary>
    public const float StormDelaySeconds = 90f;

    /// <summary>Tiles a second: about half an hour from the southern edge to the last sea.</summary>
    public const float StormSpeed = 0.47f;

    /// <summary>The storm forms just off the southern edge of the map.</summary>
    public const float StormStartBeyondEdge = 4f;

    /// <summary>A player this long in the storm draws the first hunter.</summary>
    public const float FirstHunterSeconds = 3f;
    public static readonly int FirstHunterTicks = (int)(FirstHunterSeconds * SimConstants.TickRate);

    /// <summary>After the first, another hunter comes this often while the player stays in the storm.</summary>
    public const float HunterIntervalSeconds = 8f;
    public static readonly int HunterIntervalTicks = (int)(HunterIntervalSeconds * SimConstants.TickRate);

    /// <summary>Hunters afloat for each player in the storm, at most.</summary>
    public const int MaxHuntersPerPlayer = 5;

    /// <summary>Hunters are this many levels above the sea they appear in.</summary>
    public const int HunterLevelAboveSea = 1;

    /// <summary>A hunter this far out of the storm (north of its edge) melts back into it.</summary>
    public const float VanishBeyondEdge = 2f;

    /// <summary>A hunter with no one in the storm to chase for this long melts away.</summary>
    public const float HunterIdleSeconds = 10f;
    public static readonly int HunterIdleTicks = (int)(HunterIdleSeconds * SimConstants.TickRate);

    // Spawn placement: on a ring around the player, just beyond the edge of their screen, behind them (to the south)
    // if possible, always well inside the storm, inside the map, and clear of every player and of land.
    public const float MinSpawnDistance = 24f;
    public const float MaxSpawnDistance = 32f;
    private const float SpawnArcHalfAngle = 70f * MathF.PI / 180f;
    private const float MinDepthInStorm = 3f;
    private const float EdgeInset = 3f;
    private const float MinDistanceFromPlayers = 18f;
    private const float MinDistanceFromLand = 4f;
    private const int PlacementAttempts = 40;

    private readonly Random _rng;
    private readonly float _stormStopY;

    // Per player: ticks they've been in the storm this time. Absent while they're out of it.
    private readonly Dictionary<int, int> _ticksInStorm = new();

    // Per hunter: ticks it's had no one to chase.
    private readonly Dictionary<int, int> _idleTicks = new();

    public RunDirector(int seed, Vector2 worldSize)
    {
        _rng = new Random(seed);
        StormY = worldSize.Y + StormStartBeyondEdge;
        TicksUntilStorm = (int)(StormDelaySeconds * SimConstants.TickRate);
        _stormStopY = Archipelago.Seas[^1].South;
    }

    /// <summary>World Y of the storm's leading edge; everything south of it (greater Y) is in the storm.</summary>
    public float StormY { get; private set; }

    /// <summary>Countdown until the storm starts moving; 0 once it has.</summary>
    public int TicksUntilStorm { get; private set; }

    /// <summary>Bounty hunters afloat. Kept current by <see cref="Update"/>.</summary>
    public int Hunters { get; private set; }

    /// <summary>Everything the HUD shows about what's coming.</summary>
    public RunStatus Status => new(StormY, TicksUntilStorm, Hunters);

    public bool InStorm(Vector2 position) => position.Y > StormY;

    /// <summary>Sets the counters directly, for a client mirroring the server (which runs the real director).</summary>
    public void Restore(RunStatus status)
    {
        StormY = status.StormY;
        TicksUntilStorm = status.TicksUntilStorm;
        Hunters = status.Hunters;
    }

    /// <summary>The level of hunters appearing in waters of <paramref name="seaLevel"/>.</summary>
    public static int HunterLevel(int seaLevel) => seaLevel + HunterLevelAboveSea;

    /// <summary>A bounty hunter, rather than a pirate placed on the map.</summary>
    public static bool IsHunter(Ship ship) => ship.Behavior is HunterBehavior { Relentless: true };

    public void Update(World world)
    {
        if (!world.IsRunOver)
        {
            MoveStorm();
            MeltAwayStrays(world);
            SendHunters(world);
        }
        Hunters = world.Ships.Count(s => !s.IsSunk && IsHunter(s));
    }

    private void MoveStorm()
    {
        if (TicksUntilStorm > 0)
            TicksUntilStorm--;
        else
            StormY = MathF.Max(_stormStopY, StormY - StormSpeed * SimConstants.TickDelta);
    }

    /// <summary>Hunters that have left the storm, or have had no one to chase for too long, are gone.</summary>
    private void MeltAwayStrays(World world)
    {
        List<Ship>? gone = null;
        foreach (var ship in world.Ships)
        {
            if (ship.IsSunk || ship.Behavior is not HunterBehavior { Relentless: true } hunter)
                continue;
            var idle = hunter.Target is null ? _idleTicks.GetValueOrDefault(ship.Id) + 1 : 0;
            _idleTicks[ship.Id] = idle;
            if (ship.Position.Y < StormY - VanishBeyondEdge || idle >= HunterIdleTicks)
                (gone ??= new List<Ship>()).Add(ship);
        }
        foreach (var ship in gone ?? Enumerable.Empty<Ship>())
        {
            world.RemoveShip(ship.Id);
            _idleTicks.Remove(ship.Id);
            world.Emit(new ShipHidden(world.Tick, ship.Id));
        }
    }

    /// <summary>Counts each player's time in the storm, and sends them hunters on the schedule while there's room.</summary>
    private void SendHunters(World world)
    {
        var inStorm = 0;
        var due = new List<Ship>();
        foreach (var ship in world.Ships)
        {
            if (ship.OwnerPlayerId is not { } playerId || ship.IsSunk)
                continue;
            if (!InStorm(ship.Position))
            {
                _ticksInStorm.Remove(playerId);
                continue;
            }
            inStorm++;
            var ticks = _ticksInStorm.GetValueOrDefault(playerId) + 1;
            _ticksInStorm[playerId] = ticks;
            if (ticks >= FirstHunterTicks && (ticks - FirstHunterTicks) % HunterIntervalTicks == 0)
                due.Add(ship);
        }

        var room = MaxHuntersPerPlayer * inStorm - world.Ships.Count(s => !s.IsSunk && IsHunter(s));
        foreach (var prey in due.Take(Math.Max(0, room)))
            SpawnHunter(world, prey);
    }

    private void SpawnHunter(World world, Ship prey)
    {
        var position = PickSpawnPoint(world, prey.Position);
        var toPrey = prey.Position - position;
        var hunter = world.SpawnShip(position, MathF.Atan2(toPrey.Y, toPrey.X), ShipStats.PirateSloop,
            abilities: PirateRoles.Loadout(PirateRoles.Pick(_rng)));
        hunter.Behavior = new HunterBehavior(home: position, relentless: true, prey: (w, ship) => w.Director?.InStorm(ship.Position) == true);
        hunter.Stance = NpcStance.Hunting;
        hunter.Throttle = ShipMovement.ThrottleLevels;
        PirateLevels.Apply(hunter, HunterLevel(Archipelago.LevelAt(position)));
    }

    /// <summary>
    /// A random point on the spawn ring around <paramref name="anchor"/>, south of it if possible, preferring ones far
    /// from every player and deep in the storm. Falls back to the best candidate seen if none meets every constraint.
    /// </summary>
    private Vector2 PickSpawnPoint(World world, Vector2 anchor)
    {
        var players = world.Ships.Where(s => s.Team == Team.Players).Select(s => s.Position).ToList();
        var best = Vector2.Clamp(anchor + new Vector2(0f, MinSpawnDistance), new Vector2(EdgeInset), world.WorldSize - new Vector2(EdgeInset));
        var bestScore = float.MinValue;
        for (var attempt = 0; attempt < PlacementAttempts; attempt++)
        {
            var candidate = RandomRingPoint(anchor, world.WorldSize, fullCircle: attempt >= PlacementAttempts / 2);
            var nearestPlayer = players.Min(p => Vector2.Distance(p, candidate));
            var nearestLand = world.DistanceToLand(candidate);
            var depth = candidate.Y - StormY;
            if (nearestLand < MinDistanceFromLand)
                continue; // never on (or hard against) land
            if (nearestPlayer >= MinDistanceFromPlayers && depth >= MinDepthInStorm)
                return candidate;

            var score = MathF.Min(nearestPlayer / MinDistanceFromPlayers, depth / MinDepthInStorm);
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }

    private Vector2 RandomRingPoint(Vector2 anchor, Vector2 worldSize, bool fullCircle)
    {
        // South is +Y: an angle of pi/2.
        var spread = fullCircle ? MathF.PI : SpawnArcHalfAngle;
        var angle = MathF.PI / 2f + ((float)_rng.NextDouble() * 2f - 1f) * spread;
        var distance = MinSpawnDistance + (float)_rng.NextDouble() * (MaxSpawnDistance - MinSpawnDistance);
        var point = anchor + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
        return Vector2.Clamp(point, new Vector2(EdgeInset), worldSize - new Vector2(EdgeInset));
    }
}
