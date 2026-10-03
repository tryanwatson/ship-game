using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Progression;

/// <summary>What's coming, for the HUD: computed by the server's director each tick and mirrored by clients.</summary>
/// <param name="Wave">The wave in progress (or most recently cleared); 0 before the first.</param>
/// <param name="TicksUntilNextWave">Countdown to the next wave; it only runs once <paramref name="WavePiratesLeft"/> is 0.</param>
/// <param name="WavePiratesLeft">The current wave's pirates still afloat (raiders don't count).</param>
/// <param name="NextWaveSize">Pirates in the next wave.</param>
/// <param name="Raid">Raids so far.</param>
/// <param name="TicksUntilNextRaid">Countdown to the next raid; always runs.</param>
/// <param name="NextRaidSize">Raiders the next raid will bring (fewer if the raider cap is near).</param>
public readonly record struct WaveStatus(
    int Wave, int TicksUntilNextWave, int WavePiratesLeft, int NextWaveSize, int Raid, int TicksUntilNextRaid, int NextRaidSize);

/// <summary>
/// Sends pirates in waves: once a wave is sunk, a short breather, then a bigger, tougher one. Separately, every
/// <see cref="RaidIntervalSeconds"/> a raid arrives whatever else is going on: raiders that hunt the nearest player
/// from the moment they appear, one more of them each time. Runs inside <see cref="World.Step"/> and draws from its
/// own seeded RNG, so a run is reproducible from its seed.
/// </summary>
public sealed class WaveDirector
{
    public const float FirstWaveDelaySeconds = 3f;
    public const float IntermissionSeconds = 5f;

    public const int FirstWaveSize = 2;
    public const int MaxWaveSize = 8;

    /// <summary>Each player beyond the first adds this fraction to every wave's size (and to its cap).</summary>
    public const float SizePerExtraPlayer = 0.5f;

    /// <summary>Hard ceiling on one wave, whatever the player count, to protect the server and bandwidth.</summary>
    public const int AbsoluteMaxWaveSize = 40;

    // Per wave after the first, as fractions of base stats.
    public const float HealthPerWave = 0.15f;
    public const float CooldownSpeedPerWave = 0.04f;
    public const float SpeedPerWave = 0.02f;

    public const string ScalingSource = "wave-scaling";

    /// <summary>A raid arrives this often, starting this long into the run.</summary>
    public const float RaidIntervalSeconds = 60f;
    public static readonly int RaidIntervalTicks = (int)(RaidIntervalSeconds * SimConstants.TickRate);

    /// <summary>Raiders in the first raid (before scaling for the crew); each raid after brings <see cref="RaidGrowth"/> more.</summary>
    public const int FirstRaidSize = 1;
    public const int RaidGrowth = 1;

    /// <summary>No raid spawns beyond this many raiders afloat at once, to protect the server and bandwidth.</summary>
    public const int MaxRaidersAfloat = 40;

    // Spawn placement: on a ring around a random player, just beyond the edge of their screen (so the wave
    // sails in rather than popping into view, and doesn't take half a minute to arrive on a big map), kept
    // inside the playable area, clear of every player, and spread out.
    public const float MinSpawnDistance = 30f;
    public const float MaxSpawnDistance = 40f;
    private const float EdgeInset = 3f;
    private const float MinDistanceFromPlayers = 24f;
    private const float MinDistanceBetweenSpawns = 6f;
    private const float MinDistanceFromLand = 4f;
    private const int PlacementAttempts = 40;

    private readonly Random _rng;

    public WaveDirector(int seed)
    {
        _rng = new Random(seed);
        TicksUntilNextWave = (int)(FirstWaveDelaySeconds * SimConstants.TickRate);
        TicksUntilNextRaid = RaidIntervalTicks;
        NextWaveSize = WaveSize(1);
        NextRaidSize = RaidSize(1);
    }

    /// <summary>The current wave's pirates still afloat (raiders don't count). Kept current by <see cref="Update"/>.</summary>
    public int WavePiratesLeft { get; private set; }

    /// <summary>Pirates in the next wave. Kept current by <see cref="Update"/>.</summary>
    public int NextWaveSize { get; private set; }

    /// <summary>Raiders the next raid will bring. Kept current by <see cref="Update"/>.</summary>
    public int NextRaidSize { get; private set; }

    /// <summary>Everything the HUD shows about what's coming.</summary>
    public WaveStatus Status =>
        new(Wave, TicksUntilNextWave, WavePiratesLeft, NextWaveSize, Raid, TicksUntilNextRaid, NextRaidSize);

    /// <summary>Raids so far.</summary>
    public int Raid { get; private set; }

    /// <summary>Countdown to the next raid. Always runs.</summary>
    public int TicksUntilNextRaid { get; private set; }

    /// <summary>Raiders in raid number <paramref name="raid"/> for a run of <paramref name="players"/> players.</summary>
    public static int RaidSize(int raid, int players = 1)
    {
        var scale = 1f + SizePerExtraPlayer * Math.Max(0, players - 1);
        return Math.Min((int)MathF.Ceiling((FirstRaidSize + RaidGrowth * (raid - 1)) * scale), AbsoluteMaxWaveSize);
    }

    /// <summary>A ship sent by a raid rather than a wave.</summary>
    public static bool IsRaider(Ship ship) => ship.Behavior is HunterBehavior { Relentless: true };

    /// <summary>The wave in progress (or most recently cleared); 0 before the first arrives.</summary>
    public int Wave { get; private set; }

    /// <summary>Countdown to the next wave. Only runs while no pirates are afloat.</summary>
    public int TicksUntilNextWave { get; private set; }

    /// <summary>Pirates in <paramref name="wave"/> for a run of <paramref name="players"/> players.</summary>
    /// <summary>Sets the counters directly, for a client mirroring the server (which runs the real director).</summary>
    public void Restore(WaveStatus status)
    {
        Wave = status.Wave;
        TicksUntilNextWave = status.TicksUntilNextWave;
        WavePiratesLeft = status.WavePiratesLeft;
        NextWaveSize = status.NextWaveSize;
        Raid = status.Raid;
        TicksUntilNextRaid = status.TicksUntilNextRaid;
        NextRaidSize = status.NextRaidSize;
    }

    public static int WaveSize(int wave, int players = 1)
    {
        var scale = 1f + SizePerExtraPlayer * Math.Max(0, players - 1);
        var solo = Math.Min(FirstWaveSize + wave - 1, MaxWaveSize);
        return Math.Min((int)MathF.Ceiling(solo * scale), AbsoluteMaxWaveSize);
    }

    public void Update(World world)
    {
        Advance(world);
        RefreshForecast(world);
    }

    private void Advance(World world)
    {
        if (world.IsRunOver)
            return;

        if (--TicksUntilNextRaid <= 0)
        {
            Raid++;
            SpawnRaid(world, Raid);
            TicksUntilNextRaid = RaidIntervalTicks;
        }

        // Raiders don't hold up the waves: the next wave comes once the last one's guards are sunk.
        if (AnyWavePiratesAfloat(world))
            return;

        if (TicksUntilNextWave > 0)
        {
            TicksUntilNextWave--;
            return;
        }

        Wave++;
        SpawnWave(world, Wave);
        world.Emit(new WaveStarted(world.Tick, Wave, WaveSize(Wave, world.Players.Count)));
        TicksUntilNextWave = (int)(IntermissionSeconds * SimConstants.TickRate);
    }

    private void SpawnWave(World world, int wave)
    {
        var placed = new List<Vector2>();
        var center = world.WorldSize / 2f;

        // Everyone in the run counts, including players waiting to respawn.
        for (var i = 0; i < WaveSize(wave, world.Players.Count); i++)
        {
            var position = PickSpawnPoint(world, placed);
            placed.Add(position);
            var toCenter = center - position;

            var pirate = world.SpawnShip(position, MathF.Atan2(toCenter.Y, toCenter.X), ShipStats.Sloop, abilities: Loadouts.Pirate);
            pirate.Behavior = new HunterBehavior(home: position);
            pirate.Stance = NpcStance.Guarding;
            pirate.IsAnchored = true; // guarding: rides at anchor until something comes in range
            Toughen(pirate, wave);
        }
    }

    /// <summary>Recounts what the HUD shows: the wave's pirates left, and the size of the next wave and raid.</summary>
    private void RefreshForecast(World world)
    {
        var guards = 0;
        var raiders = 0;
        foreach (var ship in world.Ships)
        {
            if (ship.Team != Team.Pirates || ship.IsSunk)
                continue;
            if (IsRaider(ship))
                raiders++;
            else
                guards++;
        }
        var players = world.Players.Count;
        WavePiratesLeft = guards;
        NextWaveSize = WaveSize(Wave + 1, players);
        NextRaidSize = Math.Min(RaidSize(Raid + 1, players), Math.Max(0, MaxRaidersAfloat - raiders));
    }

    /// <summary>Raiders: under full sail toward the nearest player from the start, toughened like the current wave.</summary>
    private void SpawnRaid(World world, int raid)
    {
        var room = MaxRaidersAfloat - world.Ships.Count(IsRaider);
        var count = Math.Min(RaidSize(raid, world.Players.Count), room);
        var placed = new List<Vector2>();
        for (var i = 0; i < count; i++)
        {
            var position = PickSpawnPoint(world, placed);
            placed.Add(position);
            var nearest = world.Ships.Where(s => s.Team == Team.Players)
                .OrderBy(s => Vector2.DistanceSquared(s.Position, position))
                .Select(s => s.Position)
                .DefaultIfEmpty(world.WorldSize / 2f)
                .First();
            var toTarget = nearest - position;

            var raider = world.SpawnShip(position, MathF.Atan2(toTarget.Y, toTarget.X), ShipStats.Sloop, abilities: Loadouts.Pirate);
            raider.Behavior = new HunterBehavior(home: position, relentless: true);
            raider.Stance = NpcStance.Hunting;
            raider.Throttle = ShipMovement.ThrottleLevels;
            Toughen(raider, Math.Max(1, Wave));
        }
    }

    /// <summary>Pirates of later waves are tougher, faster, and reload quicker.</summary>
    private static void Toughen(Ship pirate, int wave)
    {
        var scale = wave - 1;
        if (scale <= 0)
            return;
        pirate.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Percent, HealthPerWave * scale, ScalingSource));
        pirate.AddModifier(new StatModifier(StatId.CooldownSpeed, ModifierKind.Percent, CooldownSpeedPerWave * scale, ScalingSource));
        pirate.AddModifier(new StatModifier(StatId.MaxSpeed, ModifierKind.Percent, SpeedPerWave * scale, ScalingSource));
    }

    /// <summary>
    /// A random point on the spawn ring around a random player, preferring ones far from every player and from
    /// the rest of the wave. Falls back to the best candidate seen if none meets every constraint.
    /// </summary>
    private Vector2 PickSpawnPoint(World world, List<Vector2> placed)
    {
        var players = world.Ships.Where(s => s.Team == Team.Players).Select(s => s.Position).ToList();
        var best = Vector2.Zero;
        var bestScore = float.MinValue;
        for (var attempt = 0; attempt < PlacementAttempts; attempt++)
        {
            var anchor = players.Count > 0 ? players[_rng.Next(players.Count)] : world.WorldSize / 2f;
            var candidate = RandomRingPoint(anchor, world.WorldSize);
            var nearestPlayer = NearestDistance(candidate, players);
            var nearestSpawn = NearestDistance(candidate, placed);
            var nearestLand = world.DistanceToLand(candidate);
            if (nearestPlayer >= MinDistanceFromPlayers && nearestSpawn >= MinDistanceBetweenSpawns && nearestLand >= MinDistanceFromLand)
                return candidate;

            // Never settle for a spot on (or hard against) land; otherwise take the least-bad candidate.
            if (nearestLand < MinDistanceFromLand)
                continue;
            var score = MathF.Min(nearestPlayer / MinDistanceFromPlayers, nearestSpawn / MinDistanceBetweenSpawns);
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }

    private Vector2 RandomRingPoint(Vector2 anchor, Vector2 worldSize)
    {
        var angle = (float)_rng.NextDouble() * MathF.Tau;
        var distance = MinSpawnDistance + (float)_rng.NextDouble() * (MaxSpawnDistance - MinSpawnDistance);
        var point = anchor + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
        return Vector2.Clamp(point, new Vector2(EdgeInset), worldSize - new Vector2(EdgeInset));
    }

    private static float NearestDistance(Vector2 point, IEnumerable<Vector2> others)
    {
        var nearest = float.MaxValue;
        foreach (var other in others)
            nearest = MathF.Min(nearest, Vector2.Distance(point, other));
        return nearest;
    }

    private static bool AnyWavePiratesAfloat(World world)
    {
        foreach (var ship in world.Ships)
        {
            if (ship.Team == Team.Pirates && !ship.IsSunk && !IsRaider(ship))
                return true;
        }
        return false;
    }
}
