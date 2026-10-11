using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Progression;

/// <summary>Where the run is, for the HUD: kept by the server's director and mirrored by clients.</summary>
/// <param name="NodeId">The chart stop the crew is at.</param>
/// <param name="Cleared">Done there (the fortress taken, the boss sunk; a port or open water from the start): the crew can vote where next.</param>
/// <param name="FortressesTaken">Fortresses taken so far this run.</param>
/// <param name="BossesSunk">Bosses sunk so far; the run is won at <see cref="RunDirector.BossCount"/>.</param>
/// <param name="BossCountdownTicks">Countdown until the boss arrives; 0 when none is on its way.</param>
/// <param name="BossAfloat">A boss is at sea, hunting the crew.</param>
public readonly record struct RunStatus(int NodeId, bool Cleared, int FortressesTaken, int BossesSunk, int BossCountdownTicks, bool BossAfloat);

/// <summary>
/// Runs a run's voyage across its <see cref="SeaChart"/>. The crew is at one stop at a time, in a region of sea built
/// for it (see <see cref="Regions"/>). At a fortress, sinking its last fort takes it, and every player is offered cards
/// (see <see cref="CardRewards"/>). At a port, every hull is repaired on arrival and there's a shipyard to shop at. At an
/// act's end, after a <see cref="BossWarningSeconds"/> warning, the act's pirate flagship appears just out of sight of a
/// random player and hunts the crew until it's sunk: the first two pay out prismatic hands, the last wins the run.
/// Once done at a stop, each player votes (<see cref="TryVote"/>) for one of the stops it leads to; once everyone has,
/// the crew sails for the most popular (a tie is settled at random) and the old region is gone. Runs inside
/// <see cref="World.Step"/> on the server and draws from its own seeded RNG, so a run is reproducible from its seed.
/// </summary>
public sealed class RunDirector
{
    public const int BossCount = SeaChart.Acts;

    /// <summary>How long the crew is warned before a boss arrives.</summary>
    public const float BossWarningSeconds = 10f;
    public static readonly int BossWarningTicks = (int)(BossWarningSeconds * SimConstants.TickRate);

    /// <summary>
    /// Each boss's hull health, before its level and the crew's size. By the last act a crew's guns are fearsome, so the
    /// flagships are built to take it: worn down in phases (see <see cref="BossPhaseGates"/>), not sunk in a broadside.
    /// </summary>
    private static readonly float[] BossHealth = { 800f, 9000f, 52000f };

    /// <summary>
    /// The fractions of a boss's health where its phases end. Reaching one, it's untouchable a moment
    /// (<see cref="World.PhaseShiftSeconds"/>), reloads every gun at once, and calls escorts to its side.
    /// </summary>
    public static readonly IReadOnlyList<float> BossPhaseGates = new[] { 2f / 3f, 1f / 3f };

    /// <summary>
    /// How much sturdier a boss is for a crew of <paramref name="players"/>: as many times as there are sailors. Every
    /// gun in the crew can bear on one flagship, as they can't all round a fortress's shore.
    /// </summary>
    public static float BossCrewScale(int players) => Math.Max(1, players);

    /// <summary>Escorts boss <paramref name="round"/> calls at each phase's end, for a crew of one: one for the first, three for the last (more for bigger crews; see <see cref="Fortresses.CountScale"/>).</summary>
    public static int EscortsPerPhase(int round) => Math.Clamp(round, 1, BossCount);

    /// <summary>Escorts come in on a ring this far round the boss.</summary>
    public const float EscortDistance = 9f;

    // ---- Relief fleets: a fortress under siege sends for help. ----

    /// <summary>Fortresses of at least this level send for a relief fleet once half their forts are down.</summary>
    public const int ReliefLevel = 4;

    /// <summary>From this level, they send twice: a third and two thirds of the way down.</summary>
    public const int SecondReliefLevel = 6;

    /// <summary>Ships in a relief fleet for a crew of one, by the fortress's level (more for bigger crews).</summary>
    public static int ReliefShips(int level) => 2 + level / 3;

    /// <summary>How many pirates guard the start's camp, for a crew of one.</summary>
    public const int StartCampSize = 3;

    // Spawn placement: on a ring around the prey, just beyond the edge of their screen, clear of land, and as far from
    // the rest of the crew as it can manage.
    public const float MinSpawnDistance = 24f;
    public const float MaxSpawnDistance = 32f;
    private const float EdgeInset = 6f;
    private const float MinDistanceFromPlayers = 18f;
    private const float MinDistanceFromLand = 5f;
    private const int PlacementAttempts = 40;

    private readonly int _seed;
    private readonly Random _rng;
    private readonly List<int> _route = new();
    private int? _bossId;
    private int _bossPhasesAnswered;
    private int? _objectiveIslandId;
    private int _fortsAtStart;
    private int _reliefSent;
    private int _nextIslandId = 1;

    public RunDirector(int seed)
    {
        _seed = seed;
        _rng = new Random(seed);
    }

    /// <summary>The run's dice, for card draws made outside the director (rerolls).</summary>
    public Random Rng => _rng;

    /// <summary>The run's chart; null until it's drawn (<see cref="Begin"/>, or on a client, <see cref="VoyageCharted"/>).</summary>
    public SeaChart? Chart { get; set; }

    public int NodeId { get; private set; }

    /// <summary>The stop the crew is at, once there's a chart.</summary>
    public ChartNode? CurrentNode => Chart?.Find(NodeId);

    /// <summary>Every stop the crew has been to this run, in order, the current one last.</summary>
    public IReadOnlyList<int> Route => _route;

    /// <summary>Done at this stop: the crew can vote where to sail next.</summary>
    public bool Cleared { get; private set; }

    public int FortressesTaken { get; private set; }

    public int BossesSunk { get; private set; }

    /// <summary>Countdown until the boss arrives; 0 when none is on its way.</summary>
    public int BossCountdownTicks { get; private set; }

    public bool BossAfloat { get; private set; }

    /// <summary>Everything the HUD shows about the run's progress.</summary>
    public RunStatus Status => new(NodeId, Cleared, FortressesTaken, BossesSunk, BossCountdownTicks, BossAfloat);

    /// <summary>Sets the counters directly, for a client mirroring the server (which runs the real director).</summary>
    public void Restore(RunStatus status)
    {
        if (_route.Count == 0 || _route[^1] != status.NodeId)
            _route.Add(status.NodeId);
        NodeId = status.NodeId;
        Cleared = status.Cleared;
        FortressesTaken = status.FortressesTaken;
        BossesSunk = status.BossesSunk;
        BossCountdownTicks = status.BossCountdownTicks;
        BossAfloat = status.BossAfloat;
    }

    /// <summary>Boss <paramref name="round"/>'s level (from round 1): 3, 5, 7.</summary>
    public static int BossLevel(int round) => SeaChart.BossLevel(round);

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

    /// <summary>Draws the chart and puts the crew (already in <paramref name="world"/>) at its start.</summary>
    public void Begin(World world)
    {
        Chart = SeaChart.Generate(_seed);
        world.Emit(new VoyageCharted(world.Tick, Chart));
        Enter(world, Chart.Start);
    }

    public void Update(World world)
    {
        if (world.IsRunOver || CurrentNode is not { } node)
            return;
        if (!Cleared)
        {
            switch (node.Kind)
            {
                case NodeKind.Fortress:
                    TakeFortress(world, node);
                    break;
                case NodeKind.Boss:
                    TrackBoss(world, node);
                    if (!world.IsRunOver)
                        CallBoss(world, node);
                    break;
            }
            return;
        }
        SetSailIfAgreed(world);
    }

    // ---- The chart ----------------------------------------------------------------------------------------

    /// <summary>
    /// <paramref name="playerId"/> votes to sail on to <paramref name="nodeId"/>: once the crew is done here, and only
    /// somewhere the chart leads from here. A new vote replaces the old. Null on success.
    /// </summary>
    public RejectionReason? TryVote(World world, int playerId, int nodeId)
    {
        if (!world.Players.TryGetValue(playerId, out var player))
            return RejectionReason.NoShip;
        if (!Cleared || world.IsRunOver)
            return RejectionReason.NotChartingCourse;
        if (Chart is null || !Chart.Leads(NodeId, nodeId))
            return RejectionReason.UnknownCourse;
        player.CourseVote = nodeId;
        return null;
    }

    /// <summary>Sails the crew straight to <paramref name="nodeId"/>, wherever they are on the chart: for tests.</summary>
    public void SailTo(World world, int nodeId)
    {
        if (Chart?.Find(nodeId) is not { } node)
            throw new ArgumentException($"No stop {nodeId} on the chart.", nameof(nodeId));
        Enter(world, node);
    }

    /// <summary>Once every player has voted, the crew sails for the stop with the most votes (a tie settled at random).</summary>
    private void SetSailIfAgreed(World world)
    {
        if (Chart is null || world.Players.Count == 0)
            return;
        // Not while anyone's still choosing from a hand bought at the port: it'd follow them into the next fight.
        if (world.Players.Values.Any(p => p.CardOffers.Count > 0))
            return;
        var votes = world.Players.Values.Select(p => p.CourseVote).ToList();
        if (votes.Any(v => v is null || !Chart.Leads(NodeId, v.Value)))
            return;
        var tally = votes.GroupBy(v => v!.Value).Select(g => (NodeId: g.Key, Votes: g.Count())).ToList();
        var most = tally.Max(t => t.Votes);
        var leaders = tally.Where(t => t.Votes == most).Select(t => t.NodeId).Order().ToList();
        var chosen = leaders[_rng.Next(leaders.Count)];
        Enter(world, Chart.Find(chosen)!);
    }

    /// <summary>Sails the crew into <paramref name="node"/>'s region and sets it up: a fortress manned, a port's repairs, a boss's warning.</summary>
    private void Enter(World world, ChartNode node)
    {
        foreach (var player in world.Players.Values)
            player.CourseVote = null;
        var players = Math.Max(1, world.Players.Count);
        var layout = Regions.Build(node, _seed, _nextIslandId, players);
        _nextIslandId = layout.Islands.Count == 0 ? _nextIslandId : layout.Islands.Max(i => i.Id) + 1;
        world.LoadRegion(layout, node.Id);

        NodeId = node.Id;
        _route.Add(node.Id);
        _objectiveIslandId = layout.Objective;
        _bossId = null;
        _bossPhasesAnswered = 0;
        _reliefSent = 0;
        _fortsAtStart = 0;
        BossAfloat = false;
        BossCountdownTicks = 0;
        Cleared = node.Kind is NodeKind.Start or NodeKind.Port;
        // The chart says what's there, so the map shows where: the waters round the fortress or port start charted.
        if (layout.Objective is { } objective && world.FindIsland(objective) is { } goal
            && world.Discovery.RevealAround(Team.Players, goal.Center) is { Count: > 0 } cells)
            world.Emit(new AreaDiscovered(world.Tick, Team.Players, cells));
        switch (node.Kind)
        {
            case NodeKind.Start:
                ManStartCamp(world, layout, players);
                break;
            case NodeKind.Fortress when layout.Objective is { } id && world.FindIsland(id) is { } fortress:
                Fortresses.Garrison(world, fortress, players, _rng, layout.Entry, SeaChart.ExtraGuards(node.Act, node.Row, node.Difficulty));
                _fortsAtStart = Fortresses.Standing(world, fortress);
                break;
            case NodeKind.Port:
                foreach (var ship in world.Ships.Where(s => s.OwnerPlayerId is not null))
                    ship.Health = ship.Stats.MaxHealth; // the yard patches everyone up
                break;
            case NodeKind.Boss:
                BossCountdownTicks = BossWarningTicks;
                break;
        }
    }

    /// <summary>
    /// The start's sea isn't empty: a few pirates camp by one of its (rich) islands, worth seeing off for a first purse
    /// before the first port. The crew can sail on without, if they'd rather.
    /// </summary>
    private void ManStartCamp(World world, RegionLayout layout, int players)
    {
        if (layout.Islands.Count == 0)
            return;
        var camp = layout.Islands.OrderBy(i => Vector2.Distance(i.Center, layout.Entry)).Last(); // the far one
        var toEntry = Vector2.Normalize(layout.Entry - camp.Center);
        PirateCamps.SpawnPack(world, camp.ShoreToward(toEntry) + toEntry * Fortresses.GuardOffing,
            PirateCamps.CampSize(StartCampSize, players), level: 1, PirateCamps.GuardOrders(camp), _rng, layout.Entry);
    }

    // ---- Fortresses ---------------------------------------------------------------------------------------

    /// <summary>The fortress has lost its last fort: it's taken, everyone gets cards, and the crew can sail on.</summary>
    private void TakeFortress(World world, ChartNode node)
    {
        if (_objectiveIslandId is not { } islandId || world.FindIsland(islandId) is not { } island)
        {
            Cleared = true; // nothing to take (it can't happen, but don't strand the crew)
            return;
        }
        SendRelief(world, node, island);
        if (world.Ships.Any(s => s.FortIslandId == islandId && !s.IsSunk) || !world.TakeFortress(island))
            return;
        FortressesTaken++;
        Cleared = true;
        CardRewards.OfferAll(world, _rng, OfferSource.Fortress, node.Level);
    }

    /// <summary>
    /// A fortress under siege sends for help: from <see cref="ReliefLevel"/>, a fleet once half its forts are down;
    /// from <see cref="SecondReliefLevel"/>, one at a third and another at two thirds. Each comes in from the edge of the
    /// sea furthest from the crew, and hunts them.
    /// </summary>
    private void SendRelief(World world, ChartNode node, Island island)
    {
        if (_fortsAtStart == 0 || node.Level < ReliefLevel)
            return;
        var waves = node.Level >= SecondReliefLevel ? 2 : 1;
        if (_reliefSent >= waves)
            return;
        var down = _fortsAtStart - Fortresses.Standing(world, island);
        // Wave n of w is due once n/(w+1) of the forts are down: a half for one wave, a third and two thirds for two.
        if (down * (waves + 1) < (_reliefSent + 1) * _fortsAtStart)
            return;
        _reliefSent++;
        var players = Math.Max(1, world.Players.Count);
        var size = PirateCamps.CampSize(ReliefShips(node.Level), players);
        var crew = CrewCenter(world);
        var sea = world.WorldSize;
        var edges = new[]
        {
            new Vector2(ReliefEdgeInset, ReliefEdgeInset), new Vector2(sea.X / 2f, ReliefEdgeInset), new Vector2(sea.X - ReliefEdgeInset, ReliefEdgeInset),
            new Vector2(ReliefEdgeInset, sea.Y / 2f), new Vector2(sea.X - ReliefEdgeInset, sea.Y / 2f),
            new Vector2(ReliefEdgeInset, sea.Y - ReliefEdgeInset), new Vector2(sea.X - ReliefEdgeInset, sea.Y - ReliefEdgeInset),
        };
        var from = edges.Where(e => world.DistanceToLand(e) >= MinDistanceFromLand).DefaultIfEmpty(edges[1])
            .MaxBy(e => Vector2.Distance(e, crew));
        SpawnHunters(world, from, size, node.Level);
        world.Emit(new ReliefFleetSighted(world.Tick, size));
    }

    /// <summary>A relief fleet musters this far in from the edge of the sea.</summary>
    private const float ReliefEdgeInset = 10f;

    /// <summary>The middle of the crew afloat (where they came in, if nobody is).</summary>
    private static Vector2 CrewCenter(World world)
    {
        var afloat = world.Ships.Where(s => s.OwnerPlayerId is not null && !s.IsSunk).Select(s => s.Position).ToList();
        return afloat.Count == 0 ? world.RegionEntry : afloat.Aggregate(Vector2.Zero, (sum, p) => sum + p) / afloat.Count;
    }

    /// <summary><paramref name="count"/> pirates of <paramref name="level"/> round <paramref name="center"/>, facing the crew and hunting them wherever they are.</summary>
    private void SpawnHunters(World world, Vector2 center, int count, int level)
    {
        var before = world.Ships.Count;
        PirateCamps.SpawnPack(world, center, count, level, new RoamOrders(center, 0f, 1f), _rng, facing: CrewCenter(world));
        foreach (var ship in world.Ships.Skip(before))
        {
            ship.Behavior = new HunterBehavior(center, relentless: true);
            ship.Stance = NpcStance.Hunting;
        }
    }

    // ---- Bosses -------------------------------------------------------------------------------------------

    /// <summary>
    /// The boss is gone: it was sunk. The last one wins the run; the others pay out a hand of prismatics, stronger for
    /// each, and the crew can sail on into the next act.
    /// </summary>
    private void TrackBoss(World world, ChartNode node)
    {
        if (_bossId is { } afloat && world.FindShip(afloat) is { IsSunk: false } boss)
            AnswerPhases(world, boss, node);
        if (_bossId is not { } id || world.FindShip(id) is not null)
            return;
        _bossId = null;
        BossAfloat = false;
        BossesSunk++;
        Cleared = true;
        if (node.Act >= BossCount)
            world.EndRun(victory: true);
        else
        {
            CardRewards.OfferAll(world, _rng, OfferSource.Boss, CardRewards.BossDropLevel(node.Act));
            Runs.GrantLifeboats(world); // a new act: a lone sailor's lifeboat is back
        }
    }

    /// <summary>
    /// The boss has been worn to the end of a phase it hasn't answered yet: every gun reloads at once, and escorts come
    /// to its side, more for a bigger crew.
    /// </summary>
    private void AnswerPhases(World world, Ship boss, ChartNode node)
    {
        while (_bossPhasesAnswered < boss.PhasesPassed)
        {
            _bossPhasesAnswered++;
            foreach (var ability in boss.Abilities)
                ability?.Refund(1f);
            var escorts = (int)MathF.Round(EscortsPerPhase(node.Act) * Fortresses.CountScale(Math.Max(1, world.Players.Count)));
            var angle = (float)_rng.NextDouble() * MathF.Tau;
            var at = boss.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * EscortDistance;
            SpawnHunters(world, Vector2.Clamp(at, new Vector2(EdgeInset), world.WorldSize - new Vector2(EdgeInset)), escorts, node.Level);
        }
    }

    /// <summary>Counts down the warning, and sends the boss when it runs out.</summary>
    private void CallBoss(World world, ChartNode node)
    {
        if (BossAfloat || BossCountdownTicks == 0)
            return;
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
        SpawnBoss(world, prey[_rng.Next(prey.Count)], node.Act);
    }

    private void SpawnBoss(World world, Ship prey, int round)
    {
        var position = PickSpawnPoint(world, prey.Position);
        var toPrey = prey.Position - position;
        var boss = world.SpawnShip(position, MathF.Atan2(toPrey.Y, toPrey.X), BossHull(round), abilities: BossLoadout(round));
        boss.IsBoss = true;
        PirateLevels.Apply(boss, BossLevel(round));
        boss.AddCard(new CardPick("heated-shot", round)); // its hits set you burning, worse each round
        var players = Math.Max(1, world.Players.Count);
        if (players > 1)
            boss.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Multiplier, BossCrewScale(players), Fortresses.CrewSizeSource));
        Fortresses.ArmForCrew(boss, players);
        boss.Health = boss.Stats.MaxHealth;
        boss.PhaseGates = BossPhaseGates;
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
