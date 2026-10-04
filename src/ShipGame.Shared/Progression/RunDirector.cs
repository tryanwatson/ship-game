using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Progression;

/// <summary>What's coming, for the HUD: computed by the server's director each tick and mirrored by clients.</summary>
/// <param name="FortressesTaken">Fortresses taken so far this run.</param>
/// <param name="BossesSunk">Bosses sunk so far; the run is won at <see cref="RunDirector.BossCount"/>.</param>
/// <param name="BossCountdownTicks">Countdown until the next boss arrives; 0 when none is on its way.</param>
/// <param name="BossAfloat">A boss is at sea, hunting the crew.</param>
public readonly record struct RunStatus(int FortressesTaken, int BossesSunk, int BossCountdownTicks, bool BossAfloat)
{
    /// <summary>Bosses that have come so far, including one afloat.</summary>
    public int BossesSpawned => BossesSunk + (BossAfloat ? 1 : 0);

    /// <summary>Fortresses the crew must have taken before the next boss comes; 0 once every boss has come.</summary>
    public int FortressesForNextBoss =>
        BossesSpawned >= RunDirector.BossCount ? 0 : RunDirector.FortressesPerBoss * (BossesSpawned + 1);
}

/// <summary>
/// Runs a run's progress. Taking a fortress (sinking its last fort) offers every player a choice of cards (see
/// <see cref="CardRewards"/>). Every <see cref="FortressesPerBoss"/> fortresses taken calls a boss: after a
/// <see cref="BossWarningSeconds"/> warning, a pirate flagship appears just out of sight of a random player and
/// hunts the crew until it's sunk. Bosses come one at a time, each stronger than the last (a scaled-down version of
/// the full flagship to start); sinking the <see cref="BossCount"/>th wins the run. Runs inside
/// <see cref="World.Step"/> on the server and draws from its own seeded RNG, so a run is reproducible from its seed.
/// The pirates already at sea are placed by <see cref="PirateCamps"/>.
/// </summary>
public sealed class RunDirector
{
    public const int FortressesPerBoss = 2;

    public const int BossCount = 3;

    /// <summary>How long the crew is warned before a boss arrives.</summary>
    public const float BossWarningSeconds = 10f;
    public static readonly int BossWarningTicks = (int)(BossWarningSeconds * SimConstants.TickRate);

    /// <summary>Each boss's hull health, before its level; the flagship's full hull is 500.</summary>
    private static readonly float[] BossHealth = { 250f, 500f, 800f };

    /// <summary>Each player beyond the first adds this fraction to a boss's health.</summary>
    public const float BossHealthPerExtraPlayer = 0.5f;

    // Spawn placement: on a ring around the prey, just beyond the edge of their screen, clear of land, and as far from
    // the rest of the crew as it can manage.
    public const float MinSpawnDistance = 24f;
    public const float MaxSpawnDistance = 32f;
    private const float EdgeInset = 6f;
    private const float MinDistanceFromPlayers = 18f;
    private const float MinDistanceFromLand = 5f;
    private const int PlacementAttempts = 40;

    private readonly Random _rng;

    // Fortresses seen with a fort standing; one that's lost its last is taken.
    private readonly HashSet<int> _standing = new();
    private int? _bossId;

    public RunDirector(int seed)
    {
        _rng = new Random(seed);
    }

    /// <summary>The run's dice, for card draws made outside the director (rerolls).</summary>
    public Random Rng => _rng;

    public int FortressesTaken { get; private set; }

    public int BossesSunk { get; private set; }

    /// <summary>Countdown until the next boss arrives; 0 when none is on its way.</summary>
    public int BossCountdownTicks { get; private set; }

    public bool BossAfloat { get; private set; }

    /// <summary>Everything the HUD shows about the run's progress.</summary>
    public RunStatus Status => new(FortressesTaken, BossesSunk, BossCountdownTicks, BossAfloat);

    /// <summary>Sets the counters directly, for a client mirroring the server (which runs the real director).</summary>
    public void Restore(RunStatus status)
    {
        FortressesTaken = status.FortressesTaken;
        BossesSunk = status.BossesSunk;
        BossCountdownTicks = status.BossCountdownTicks;
        BossAfloat = status.BossAfloat;
    }

    /// <summary>Boss <paramref name="round"/>'s level (from round 1): 3, 5, 7.</summary>
    public static int BossLevel(int round) => 1 + 2 * Math.Clamp(round, 1, BossCount);

    /// <summary>Boss <paramref name="round"/>'s hull: the flagship's, with the round's health.</summary>
    public static ShipStats BossHull(int round) => ShipStats.Flagship with { MaxHealth = BossHealth[Math.Clamp(round, 1, BossCount) - 1] };

    /// <summary>Every boss has broadsides; the second adds a mortar, the last a long gun as well.</summary>
    public static IReadOnlyList<Ability?> BossLoadout(int round) => new Ability?[]
    {
        WeaponCatalog.Broadside.Ability,
        round >= 2 ? WeaponCatalog.Mortar.Ability : null,
        round >= 3 ? WeaponCatalog.LongGun.Ability : null,
        null,
    };

    public void Update(World world)
    {
        if (world.IsRunOver)
            return;
        TakeFallenFortresses(world);
        TrackBoss(world);
        if (!world.IsRunOver)
            CallBoss(world);
    }

    /// <summary>A fortress that had forts standing and now has none is taken: everyone gets cards.</summary>
    private void TakeFallenFortresses(World world)
    {
        var manned = new HashSet<int>();
        foreach (var ship in world.Ships)
        {
            if (ship.FortIslandId is { } islandId && !ship.IsSunk)
                manned.Add(islandId);
        }
        _standing.UnionWith(manned);

        foreach (var islandId in _standing.Where(id => !manned.Contains(id)).Order().ToList())
        {
            _standing.Remove(islandId);
            if (world.FindIsland(islandId) is not { } island || !world.TakeFortress(island))
                continue;
            FortressesTaken++;
            Trading.Contracts.OpenPost(world, island); // a port now
            CardRewards.OfferAll(world, _rng, OfferSource.Fortress, island.Level);
        }
    }

    /// <summary>
    /// The boss is gone: it was sunk. The last one wins the run; the others pay out a hand of prismatics, stronger
    /// for each (they don't count toward the next boss).
    /// </summary>
    private void TrackBoss(World world)
    {
        if (_bossId is not { } id || world.FindShip(id) is not null)
            return;
        _bossId = null;
        BossAfloat = false;
        BossesSunk++;
        if (BossesSunk >= BossCount)
            world.EndRun(victory: true);
        else
            CardRewards.OfferAll(world, _rng, OfferSource.Boss, CardRewards.BossDropLevel(BossesSunk));
    }

    /// <summary>Starts the warning once enough fortresses have fallen, and sends the boss when it runs out.</summary>
    private void CallBoss(World world)
    {
        if (BossAfloat || Status.BossesSpawned >= BossCount)
            return;
        if (BossCountdownTicks == 0)
        {
            if (FortressesTaken >= Status.FortressesForNextBoss)
                BossCountdownTicks = BossWarningTicks;
            return;
        }
        if (BossCountdownTicks > 1)
        {
            BossCountdownTicks--;
            return;
        }

        // Due now, but only once there's someone afloat to hunt.
        var prey = world.Ships.Where(s => s.OwnerPlayerId is not null && !s.IsSunk).OrderBy(s => s.Id).ToList();
        if (prey.Count == 0)
            return;
        BossCountdownTicks = 0;
        SpawnBoss(world, prey[_rng.Next(prey.Count)], Status.BossesSpawned + 1);
    }

    private void SpawnBoss(World world, Ship prey, int round)
    {
        var position = PickSpawnPoint(world, prey.Position);
        var toPrey = prey.Position - position;
        var boss = world.SpawnShip(position, MathF.Atan2(toPrey.Y, toPrey.X), BossHull(round), abilities: BossLoadout(round));
        boss.IsBoss = true;
        PirateLevels.Apply(boss, BossLevel(round));
        var players = Math.Max(1, world.Players.Count);
        if (players > 1)
            boss.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Multiplier, 1f + BossHealthPerExtraPlayer * (players - 1),
                Fortresses.CrewSizeSource));
        boss.Health = boss.Stats.MaxHealth;
        boss.Behavior = new HunterBehavior(position, relentless: true);
        boss.Stance = NpcStance.Hunting;
        boss.Throttle = ShipMovement.ThrottleLevels;
        _bossId = boss.Id;
        BossAfloat = true;
        world.Emit(new BossSpawned(world.Tick, boss.Id, round, prey.OwnerPlayerId ?? 0));
    }

    /// <summary>
    /// A random point on the spawn ring around <paramref name="anchor"/>, clear of land and inside the map, preferring
    /// ones far from every other player. Falls back to the best candidate seen if none meets every constraint.
    /// </summary>
    private Vector2 PickSpawnPoint(World world, Vector2 anchor)
    {
        var players = world.Ships.Where(s => s.Team == Team.Players).Select(s => s.Position).ToList();
        var best = anchor;
        var bestScore = float.MinValue;
        for (var attempt = 0; attempt < PlacementAttempts; attempt++)
        {
            var angle = (float)_rng.NextDouble() * MathF.Tau;
            var distance = MinSpawnDistance + (float)_rng.NextDouble() * (MaxSpawnDistance - MinSpawnDistance);
            var candidate = Vector2.Clamp(anchor + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance,
                new Vector2(EdgeInset), world.WorldSize - new Vector2(EdgeInset));
            var nearestPlayer = players.Count == 0 ? float.MaxValue : players.Min(p => Vector2.Distance(p, candidate));
            var nearestLand = world.DistanceToLand(candidate);
            if (nearestLand >= MinDistanceFromLand && nearestPlayer >= MinDistanceFromPlayers)
                return candidate;

            var score = MathF.Min(nearestLand / MinDistanceFromLand, nearestPlayer / MinDistanceFromPlayers);
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }
}
