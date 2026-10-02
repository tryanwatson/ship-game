using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Progression;

/// <summary>
/// Sends pirates in waves: once a wave is sunk, a short breather, then a bigger, tougher one. Runs inside
/// <see cref="World.Step"/> and draws from its own seeded RNG, so a run is reproducible from its seed.
/// </summary>
public sealed class WaveDirector
{
    public const float FirstWaveDelaySeconds = 3f;
    public const float IntermissionSeconds = 5f;

    public const int FirstWaveSize = 2;
    public const int MaxWaveSize = 8;

    // Per wave after the first, as fractions of base stats.
    public const float HealthPerWave = 0.15f;
    public const float CooldownSpeedPerWave = 0.04f;
    public const float SpeedPerWave = 0.02f;

    public const string ScalingSource = "wave-scaling";

    // Spawn placement: on a ring around a random player, just beyond the edge of their screen (so the wave
    // sails in rather than popping into view, and doesn't take half a minute to arrive on a big map), kept
    // inside the playable area, clear of every player, and spread out.
    public const float MinSpawnDistance = 30f;
    public const float MaxSpawnDistance = 40f;
    private const float EdgeInset = 3f;
    private const float MinDistanceFromPlayers = 24f;
    private const float MinDistanceBetweenSpawns = 6f;
    private const int PlacementAttempts = 40;

    private readonly Random _rng;

    public WaveDirector(int seed)
    {
        _rng = new Random(seed);
        TicksUntilNextWave = (int)(FirstWaveDelaySeconds * SimConstants.TickRate);
    }

    /// <summary>The wave in progress (or most recently cleared); 0 before the first arrives.</summary>
    public int Wave { get; private set; }

    /// <summary>Countdown to the next wave. Only runs while no pirates are afloat.</summary>
    public int TicksUntilNextWave { get; private set; }

    public static int WaveSize(int wave) => Math.Min(FirstWaveSize + wave - 1, MaxWaveSize);

    public void Update(World world)
    {
        if (AnyPiratesAfloat(world))
            return;

        if (TicksUntilNextWave > 0)
        {
            TicksUntilNextWave--;
            return;
        }

        Wave++;
        SpawnWave(world, Wave);
        TicksUntilNextWave = (int)(IntermissionSeconds * SimConstants.TickRate);
    }

    private void SpawnWave(World world, int wave)
    {
        var placed = new List<Vector2>();
        var center = world.WorldSize / 2f;

        for (var i = 0; i < WaveSize(wave); i++)
        {
            var position = PickSpawnPoint(world, placed);
            placed.Add(position);
            var toCenter = center - position;

            var pirate = world.SpawnShip(position, MathF.Atan2(toCenter.Y, toCenter.X), ShipStats.Sloop, abilities: Loadouts.Sloop);
            pirate.Behavior = new HunterBehavior(home: position);
            pirate.IsAnchored = true; // guarding: rides at anchor until something comes in range

            var scale = wave - 1;
            if (scale > 0)
            {
                pirate.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Percent, HealthPerWave * scale, ScalingSource));
                pirate.AddModifier(new StatModifier(StatId.CooldownSpeed, ModifierKind.Percent, CooldownSpeedPerWave * scale, ScalingSource));
                pirate.AddModifier(new StatModifier(StatId.MaxSpeed, ModifierKind.Percent, SpeedPerWave * scale, ScalingSource));
            }
        }
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
            if (nearestPlayer >= MinDistanceFromPlayers && nearestSpawn >= MinDistanceBetweenSpawns)
                return candidate;

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

    private static bool AnyPiratesAfloat(World world)
    {
        foreach (var ship in world.Ships)
        {
            if (ship.Team == Team.Pirates && !ship.IsSunk)
                return true;
        }
        return false;
    }
}
