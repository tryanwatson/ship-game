using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Tests;

public class RunDirectorTests
{
    private const int PlayerId = 1;

    /// <summary>
    /// A real run, set sailing: everyone has chosen their starting card and a long gun, and can't be sunk (unless
    /// <paramref name="mortal"/>). The crew is at the start, in open water, ready to chart a course.
    /// </summary>
    private static (World world, RunDirector director) CreateRun(int players = 1, int seed = 7, bool mortal = false)
    {
        var world = Runs.Create(seed, Enumerable.Range(1, players).Select(id => (id, $"SAILOR {id}")).ToList());
        world.Wind = Vector2.Zero;
        ChooseAll(world);
        foreach (var id in world.Players.Keys)
            world.Enqueue(new ChooseStartingWeaponCommand(id, LongGun.AbilityId));
        world.Step();
        if (!mortal)
            foreach (var ship in world.Ships.Where(s => s.OwnerPlayerId is not null))
                Invulnerable(ship);
        world.DrainEvents();
        return (world, world.Director!);
    }

    private static void Invulnerable(Ship ship)
    {
        ship.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 1_000_000f, "test"));
        ship.Health = ship.Stats.MaxHealth;
    }

    /// <summary>Everyone takes the first card of every offer waiting, which ends the pause.</summary>
    private static void ChooseAll(World world)
    {
        while (world.Players.Values.Any(p => p.CardOffers.Count > 0))
        {
            foreach (var player in world.Players.Values.Where(p => p.CardOffers.Count > 0))
                world.Enqueue(new ChooseCardCommand(player.PlayerId, player.CardOffers[0].Cards[0].Id));
            world.Step();
        }
    }

    /// <summary>Sinks every fort standing (the fortress then falls, and the game pauses for cards).</summary>
    private static void Raze(World world)
    {
        foreach (var fort in world.Ships.Where(s => s.IsFort))
            fort.Health = 0f;
        world.Step();
    }

    /// <summary>Everyone votes for <paramref name="nodeId"/>, and the crew sails there.</summary>
    private static void SailTo(World world, int nodeId)
    {
        foreach (var id in world.Players.Keys)
            world.Enqueue(new ChooseCourseCommand(id, nodeId));
        world.Step();
    }

    /// <summary>Whatever it takes to be done at the current stop: the fortress razed, the boss called and sunk, the cards chosen.</summary>
    private static void Clear(World world)
    {
        var director = world.Director!;
        if (director.Cleared)
            return;
        if (director.CurrentNode!.Kind == NodeKind.Fortress)
            Raze(world);
        else if (director.CurrentNode.Kind == NodeKind.Boss)
        {
            RunTicks(world, director.BossCountdownTicks + 1);
            Boss(world)!.Health = 0f;
            world.Step();
        }
        ChooseAll(world);
    }

    /// <summary>The stop the current one leads to that matches <paramref name="pick"/> (the first, if none does).</summary>
    private static ChartNode Next(World world, Func<ChartNode, bool>? pick = null)
    {
        var director = world.Director!;
        var options = director.CurrentNode!.Next.Select(id => director.Chart!.Find(id)!).ToList();
        return options.FirstOrDefault(pick ?? (_ => true)) ?? options[0];
    }

    /// <summary>Plays on (clearing each stop and sailing to the first stop it leads to) until the crew is at act <paramref name="act"/>'s boss.</summary>
    private static void PlayToBoss(World world, int act)
    {
        var director = world.Director!;
        while (director.CurrentNode is not { Kind: NodeKind.Boss } boss || boss.Act != act)
        {
            Clear(world);
            SailTo(world, Next(world).Id);
        }
    }

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    private static Ship? Boss(World world) => world.Ships.SingleOrDefault(s => s.IsBoss);

    // ---- The chart ----------------------------------------------------------------------------------------

    [Fact]
    public void TheChart_RunsThroughThreeActs_EachEndingInABoss()
    {
        var chart = SeaChart.Generate(seed: 3);

        Assert.Equal(NodeKind.Start, chart.Start.Kind);
        Assert.Equal(1 + SeaChart.Acts * (SeaChart.RowsPerAct * SeaChart.Lanes + 1), chart.Nodes.Count);
        var bosses = chart.Nodes.Where(n => n.Kind == NodeKind.Boss).OrderBy(n => n.Act).ToList();
        Assert.Equal(Enumerable.Range(1, SeaChart.Acts), bosses.Select(b => b.Act));
        Assert.Equal(new[] { 3, 5, 7 }, bosses.Select(b => b.Level));
        Assert.Empty(bosses[^1].Next); // the voyage ends there
        Assert.All(chart.Nodes.Where(n => n != bosses[^1]), n => Assert.NotEmpty(n.Next));

        // Every stop can be reached from the start.
        var reached = new HashSet<int> { chart.Start.Id };
        var frontier = new Queue<int>(reached);
        while (frontier.TryDequeue(out var id))
        {
            foreach (var next in chart.Find(id)!.Next.Where(reached.Add))
                frontier.Enqueue(next);
        }
        Assert.Equal(chart.Nodes.Select(n => n.Id).Order(), reached.Order());
    }

    [Fact]
    public void EachRow_OffersFortressesOfMixedDifficulty_AndLaterRowsAPort()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var chart = SeaChart.Generate(seed);
            foreach (var row in chart.Nodes.Where(n => n.Kind is NodeKind.Fortress or NodeKind.Port).GroupBy(n => (n.Act, n.Row)))
            {
                var (act, index) = row.Key;
                Assert.Equal(SeaChart.Lanes, row.Count());
                Assert.Equal(SeaChart.HasPort(index) ? 1 : 0, row.Count(n => n.Kind == NodeKind.Port));
                var fortresses = row.Where(n => n.Kind == NodeKind.Fortress).ToList();
                Assert.Equal(fortresses.Count, fortresses.Select(f => f.Difficulty).Distinct().Count());
                Assert.All(fortresses, f => Assert.Equal(SeaChart.FortressLevel(act, index, f.Difficulty), f.Level));
            }
        }
        // A level a step, and deeper in, harder: the calmest of the last act is past the first act's direst.
        Assert.Equal(SeaChart.FortressLevel(1, 1, Difficulty.Calm) + 2, SeaChart.FortressLevel(1, 1, Difficulty.Dire));
        // Except the very first row, gentler for crews with nothing but a starting card: Rough is guards, not a level.
        Assert.Equal((1, 1, 2), (SeaChart.FortressLevel(1, 0, Difficulty.Calm), SeaChart.FortressLevel(1, 0, Difficulty.Rough),
            SeaChart.FortressLevel(1, 0, Difficulty.Dire)));
        Assert.Equal((0, SeaChart.GuardsPerStep), (SeaChart.ExtraGuards(1, 0, Difficulty.Calm), SeaChart.ExtraGuards(1, 0, Difficulty.Rough)));
        Assert.Equal(0, SeaChart.ExtraGuards(2, 0, Difficulty.Dire));
        Assert.True(SeaChart.FortressLevel(3, 0, Difficulty.Calm) > SeaChart.FortressLevel(1, 0, Difficulty.Dire));
        Assert.Equal(8, SeaChart.FortressLevel(SeaChart.Acts, SeaChart.RowsPerAct - 1, Difficulty.Dire)); // the top of the card table
    }

    [Fact]
    public void EveryStop_LeadsToItsOwnLaneAndTheOnesBeside_InTheNextRow()
    {
        var chart = SeaChart.Generate(seed: 11);
        foreach (var node in chart.Nodes.Where(n => n.Kind is NodeKind.Fortress or NodeKind.Port))
        {
            var next = node.Next.Select(id => chart.Find(id)!).ToList();
            if (node.Row == SeaChart.RowsPerAct - 1)
            {
                Assert.Equal(NodeKind.Boss, Assert.Single(next).Kind);
                continue;
            }
            Assert.All(next, n => Assert.Equal((node.Act, node.Row + 1), (n.Act, n.Row)));
            Assert.Equal(Enumerable.Range(node.Lane - 1, 3).Where(l => l is >= 0 and < SeaChart.Lanes), next.Select(n => n.Lane).Order());
        }
    }

    [Fact]
    public void TheChart_IsTheSameForTheSameSeed()
    {
        Assert.Equal(SeaChart.Generate(5).Nodes, SeaChart.Generate(5).Nodes);
        Assert.NotEqual(SeaChart.Generate(5).Nodes, SeaChart.Generate(6).Nodes);
    }

    // ---- Setting out, and voting --------------------------------------------------------------------------

    [Fact]
    public void ARun_OpensAtTheStart_InOpenWater_ReadyToChartACourse()
    {
        var (world, director) = CreateRun(players: 3);

        Assert.Equal(director.Chart!.Start.Id, director.NodeId);
        Assert.True(director.Cleared);
        Assert.Equal(Regions.StartSize, world.WorldSize);
        Assert.NotEmpty(world.Islands);
        // Only a small camp of the weakest pirates, by an island well off the entry: a first purse, if the crew wants it.
        var pirates = world.Ships.Where(s => s.OwnerPlayerId is null).ToList();
        Assert.Equal(PirateCamps.CampSize(RunDirector.StartCampSize, 3), pirates.Count);
        Assert.All(pirates, p => Assert.Equal(1, p.Level));
        var ships = world.Ships.Where(s => s.OwnerPlayerId is not null).OrderBy(s => s.Position.X).ToList();
        Assert.Equal(3, ships.Count);
        Assert.All(ships, s => Assert.Equal(world.WorldSize.Y - Regions.EntryOffing, s.Position.Y, 3));
        Assert.All(ships, s => Assert.True(world.DistanceToLand(s.Position) > 5f));
        Assert.All(ships, s => Assert.Equal(Regions.EntryHeading, s.Heading));
        Assert.Equal(world.WorldSize.X / 2f, ships[1].Position.X, 3); // abreast, the middle one in the middle
    }

    [Fact]
    public void ARun_Opens_Unarmed_AndPaused_ForTheStartingCard()
    {
        var world = Runs.Create(seed: 1, new[] { (PlayerId, "ANNE"), (2, "BEN") });
        Assert.All(world.Ships.Where(s => s.OwnerPlayerId is not null), s => Assert.All(s.Abilities, Assert.Null)); // the weapon's chosen once the run opens
        Assert.True(world.IsPaused);
        Assert.True(world.Director!.Cleared); // the crew can vote while it waits
        Assert.Equal(new[] { typeof(VoyageCharted), typeof(RegionEntered) },
            world.DrainEvents().Where(e => e is VoyageCharted or RegionEntered).Select(e => e.GetType()));
    }

    [Fact]
    public void TheCrew_SetsSail_OnlyOnceEveryoneHasVoted()
    {
        var (world, director) = CreateRun(players: 2);
        var start = director.NodeId;
        var course = Next(world);

        world.Enqueue(new ChooseCourseCommand(1, course.Id));
        RunTicks(world, 5);
        Assert.Equal(start, director.NodeId);
        Assert.Equal(course.Id, world.Players[1].CourseVote);

        world.Enqueue(new ChooseCourseCommand(2, course.Id));
        world.Step();
        Assert.Equal(course.Id, director.NodeId);
        Assert.All(world.Players.Values, p => Assert.Null(p.CourseVote)); // votes start over at each stop
    }

    [Fact]
    public void Votes_OnlyGoWhereTheChartLeads_AndOnlyOnceDoneHere()
    {
        var (world, director) = CreateRun();
        var afar = director.Chart!.Nodes.First(n => n.Kind == NodeKind.Boss);

        Assert.Equal(RejectionReason.UnknownCourse, director.TryVote(world, PlayerId, afar.Id));
        Assert.Equal(RejectionReason.UnknownCourse, director.TryVote(world, PlayerId, director.NodeId));

        SailTo(world, Next(world, n => n.Kind == NodeKind.Fortress).Id);
        Assert.False(director.Cleared);
        Assert.Equal(RejectionReason.NotChartingCourse, director.TryVote(world, PlayerId, Next(world).Id));
        world.DrainEvents();
        world.Enqueue(new ChooseCourseCommand(PlayerId, Next(world).Id));
        world.Step();
        Assert.Equal(RejectionReason.NotChartingCourse, world.DrainEvents().OfType<CommandRejected>().Single().Reason);
    }

    [Fact]
    public void TheMostVotes_Win_AndATieIsSettledBetweenTheTied()
    {
        var (world, director) = CreateRun(players: 3);
        var options = director.CurrentNode!.Next;
        world.Enqueue(new ChooseCourseCommand(1, options[0]));
        world.Enqueue(new ChooseCourseCommand(2, options[1]));
        world.Enqueue(new ChooseCourseCommand(3, options[1]));
        world.Step();
        Assert.Equal(options[1], director.NodeId);

        var tied = new HashSet<int>();
        for (var seed = 0; seed < 12; seed++)
        {
            var (split, splitDirector) = CreateRun(players: 2, seed: seed);
            var choices = splitDirector.CurrentNode!.Next;
            split.Enqueue(new ChooseCourseCommand(1, choices[0]));
            split.Enqueue(new ChooseCourseCommand(2, choices[^1]));
            split.Step();
            Assert.Contains(splitDirector.NodeId, new[] { choices[0], choices[^1] });
            tied.Add(splitDirector.CurrentNode!.Lane);
        }
        Assert.Equal(2, tied.Count); // either can win
    }

    [Fact]
    public void ASunkPlayer_StillVotes_AndComesBackWithTheCrewInTheNextRegion()
    {
        var (world, director) = CreateRun(players: 2, mortal: true);
        var lost = world.GetPlayerShip(2)!;
        lost.Health = 0f;
        world.Step();
        Assert.True(world.Players[2].IsAwaitingRespawn);

        SailTo(world, Next(world).Id);

        Assert.Equal(director.CurrentNode!.Id, director.Route[^1]);
        var back = world.GetPlayerShip(2)!;
        Assert.NotNull(back);
        Assert.False(world.Players[2].IsAwaitingRespawn);
        Assert.Equal(world.WorldSize.Y - Regions.EntryOffing, back.Position.Y, 3);
    }

    // ---- Stops ----------------------------------------------------------------------------------------------

    [Fact]
    public void AFortress_IsMannedForItsLevel_AndTakingIt_OffersCardsAtThatLevel()
    {
        var (world, director) = CreateRun(players: 2);
        var dire = Next(world, n => n.Kind == NodeKind.Fortress && n.Difficulty == Difficulty.Dire);
        SailTo(world, dire.Id);

        var fortress = Assert.Single(world.Islands, i => i.IsFortress);
        Assert.Equal(dire.Level, fortress.Level);
        Assert.True(world.Discovery.IsDiscovered(Team.Players, fortress)); // charted from the start, on the map
        Assert.Equal(Fortresses.Forts(dire.Level, players: 2), world.Ships.Count(s => s.FortIslandId == fortress.Id));
        Assert.All(world.Ships.Where(s => s.Team == Team.Pirates), p => Assert.Equal(dire.Level, p.Level));
        Assert.False(director.Cleared);

        // One fort standing holds it.
        world.Ships.First(s => s.IsFort).Health = 0f;
        RunTicks(world, 2);
        Assert.True(world.IsHeld(fortress));
        Assert.False(director.Cleared);

        world.DrainEvents();
        Raze(world);
        Assert.False(world.IsHeld(fortress));
        Assert.True(director.Cleared);
        Assert.Equal(1, director.FortressesTaken);
        var events = world.DrainEvents();
        Assert.Equal(fortress.Id, Assert.Single(events.OfType<FortressTaken>()).IslandId);
        Assert.Equal(new[] { 1, 2 }, events.OfType<CardsOffered>().Select(e => e.PlayerId).Order());
        foreach (var player in world.Players.Values)
        {
            var offer = Assert.Single(player.CardOffers);
            Assert.Equal((OfferSource.Fortress, dire.Level), (offer.Source, offer.Level));
            Assert.Equal(offer.Cards.Count, offer.Cards.Select(c => c.Id).Distinct().Count());
        }
        Assert.False(world.IsPort(fortress)); // ports are stops of their own
    }

    [Fact]
    public void HarderFortresses_HaveMoreGuns()
    {
        var (world, director) = CreateRun();
        var calm = Next(world, n => n.Difficulty == Difficulty.Calm);
        var dire = Next(world, n => n.Difficulty == Difficulty.Dire);
        SailTo(world, calm.Id);
        var calmGuns = world.Ships.Count(s => s.IsFort);
        director.SailTo(world, dire.Id);
        Assert.True(world.Ships.Count(s => s.IsFort) > calmGuns);
    }

    [Fact]
    public void SailingOn_LeavesTheOldRegionBehind_AndTheHullAsItWas()
    {
        var (world, director) = CreateRun(mortal: true);
        SailTo(world, Next(world, n => n.Kind == NodeKind.Fortress).Id);
        var oldIslands = world.Islands.Select(i => i.Id).ToList();
        var ship = world.GetPlayerShip(PlayerId)!;
        Raze(world);
        ChooseAll(world);
        ship.Health = ship.Stats.MaxHealth / 2f;
        world.DrainEvents();

        var course = Next(world, n => n.Kind == NodeKind.Fortress);
        var regions = world.RegionsEntered;
        SailTo(world, course.Id);
        Assert.Equal(regions + 1, world.RegionsEntered); // what was drawn of the old region knows to start over

        var entered = Assert.Single(world.DrainEvents().OfType<RegionEntered>());
        Assert.Equal((course.Id, world.WorldSize), (entered.NodeId, entered.Size));
        Assert.Equal(world.Islands.Select(i => i.Id), entered.Islands.Select(i => i.Id));
        Assert.Empty(world.Islands.Select(i => i.Id).Intersect(oldIslands)); // island ids never repeat in a run
        Assert.All(world.Ships.Where(s => s.OwnerPlayerId is null), s => Assert.True(s.Id >= entered.FirstEntityId));
        Assert.Empty(world.Projectiles);
        Assert.Same(ship, world.GetPlayerShip(PlayerId));
        Assert.InRange(ship.Health, ship.Stats.MaxHealth / 2f, ship.Stats.MaxHealth * 0.6f); // a port repairs it, sailing on doesn't
        Assert.Equal((0f, 0, Regions.EntryHeading), (ship.Speed, ship.Throttle, ship.Heading));
        Assert.Equal(new[] { director.Chart!.Start.Id, director.Route[1], course.Id }, director.Route);
    }

    [Fact]
    public void APort_RepairsEveryHull_AndHasAShipyard()
    {
        var (world, director) = CreateRun(players: 2, mortal: true);
        while (Next(world).Kind != NodeKind.Port && !director.CurrentNode!.Next.Any(id => director.Chart!.Find(id)!.Kind == NodeKind.Port))
        {
            Clear(world);
            SailTo(world, Next(world).Id);
        }
        Clear(world);
        foreach (var ship in world.Ships.Where(s => s.OwnerPlayerId is not null))
            ship.Health = 1f;

        SailTo(world, Next(world, n => n.Kind == NodeKind.Port).Id);

        Assert.True(director.Cleared); // nothing to fight: the crew can sail on whenever it likes
        Assert.All(world.Ships, s => Assert.Equal(s.Stats.MaxHealth, s.Health));
        var yard = Assert.Single(world.Islands, i => i.HasShipyard);
        Assert.True(world.IsPort(yard));
        Assert.DoesNotContain(world.Ships, s => s.Team == Team.Pirates);
    }

    // ---- Bosses ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheBoss_ComesAfterAWarning_NearAPlayer()
    {
        var (world, director) = CreateRun(players: 3);
        PlayToBoss(world, act: 1);
        Assert.False(director.Cleared);
        Assert.InRange(director.BossCountdownTicks, RunDirector.BossWarningTicks - 1, RunDirector.BossWarningTicks);
        Assert.Equal(Regions.BossSize, world.WorldSize);
        world.DrainEvents();

        RunTicks(world, director.BossCountdownTicks - 1);
        Assert.Null(Boss(world));
        RunTicks(world, 1);

        var boss = Boss(world)!;
        Assert.True(director.BossAfloat);
        Assert.Equal(0, director.BossCountdownTicks);
        Assert.Equal(RunDirector.BossLevel(1), boss.Level);
        Assert.Equal(RunDirector.BossHull(1), boss.BaseStats);
        Assert.Equal(boss.Stats.MaxHealth, boss.Health);
        Assert.True(((HunterBehavior)boss.Behavior!).Relentless);
        var spawned = Assert.Single(world.DrainEvents().OfType<BossSpawned>());
        Assert.Equal((boss.Id, 1), (spawned.ShipId, spawned.Round));
        var prey = world.GetPlayerShip(spawned.PreyPlayerId)!;
        Assert.InRange(Vector2.Distance(prey.Position, boss.Position), RunDirector.MinSpawnDistance - 0.01f, RunDirector.MaxSpawnDistance + 0.01f);
    }

    [Fact]
    public void Bosses_PayOutPrismatics_StrongerEachAct_AndLeadIntoTheNextAct()
    {
        var (world, director) = CreateRun(players: 2);
        Ship? last = null;
        foreach (var act in new[] { 1, 2 })
        {
            PlayToBoss(world, act);
            RunTicks(world, director.BossCountdownTicks + 1);
            var boss = Boss(world)!;
            if (last is not null)
            {
                Assert.True(boss.Level > last.Level);
                Assert.True(boss.Stats.MaxHealth > last.Stats.MaxHealth);
                Assert.True(boss.HasAbility(Mortar.AbilityId) && !last.HasAbility(Mortar.AbilityId));
            }
            last = boss;
            boss.Health = 0f;
            world.Step();

            Assert.Equal(act, director.BossesSunk);
            Assert.True(director.Cleared);
            Assert.False(world.IsRunOver);
            foreach (var player in world.Players.Values)
            {
                var offer = Assert.Single(player.CardOffers);
                Assert.Equal((OfferSource.Boss, CardRewards.BossDropLevel(act)), (offer.Source, offer.Level));
                Assert.All(offer.Cards, c => Assert.Equal((CardTier.Prismatic, offer.Level), (c.Definition.Tier, c.Level)));
            }
            ChooseAll(world);
            Assert.All(director.CurrentNode!.Next, id => Assert.Equal((act + 1, 0), (director.Chart!.Find(id)!.Act, director.Chart.Find(id)!.Row)));
        }
        Assert.True(CardRewards.BossDropLevel(2) > CardRewards.BossDropLevel(1));
    }

    [Fact]
    public void SinkingTheLastBoss_WinsTheRun()
    {
        var (world, director) = CreateRun();
        director.SailTo(world, director.Chart!.Nodes.Single(n => n.Kind == NodeKind.Boss && n.Act == SeaChart.Acts).Id);
        RunTicks(world, director.BossCountdownTicks + 1);
        var boss = Boss(world)!;
        Assert.Equal(RunDirector.BossLevel(3), boss.Level);
        Assert.All(new[] { BroadsideVolley.AbilityId, Mortar.AbilityId, LongGun.AbilityId }, id => Assert.True(boss.HasAbility(id)));
        var gold = world.Players[PlayerId].Gold;
        world.DrainEvents();

        boss.Health = 0f;
        boss.LastHitByShipId = world.GetPlayerShip(PlayerId)!.Id;
        world.Step();

        Assert.True(world.IsRunOver);
        Assert.True(world.IsVictory);
        Assert.True(Assert.Single(world.DrainEvents().OfType<RunEnded>()).Victory);
        Assert.Empty(world.Players[PlayerId].CardOffers); // nothing to choose: it's won
        Assert.Equal(gold + KillRewards.GoldFor(boss), world.Players[PlayerId].Gold);
    }

    [Fact]
    public void Bosses_AreSturdierForBiggerCrews()
    {
        static float BossHealth(int players)
        {
            var (world, director) = CreateRun(players);
            director.SailTo(world, director.Chart!.Nodes.First(n => n.Kind == NodeKind.Boss).Id);
            RunTicks(world, director.BossCountdownTicks + 1);
            return Boss(world)!.Stats.MaxHealth;
        }

        Assert.Equal(BossHealth(1) * (RunDirector.BossCrewScale(4)), BossHealth(4), 2);
    }

    [Fact]
    public void Bosses_NeverSpawnOnOrAgainstLand()
    {
        // Players parked off every island of boss arenas in turn: the boss still finds open water.
        for (var seed = 0; seed < 6; seed++)
        {
            var (world, director) = CreateRun(seed: seed);
            var arena = director.Chart!.Nodes.First(n => n.Kind == NodeKind.Boss).Id;
            director.SailTo(world, arena);
            for (var i = 0; i < world.Islands.Count; i++)
            {
                director.SailTo(world, arena); // the same arena afresh, the warning starting over
                var island = world.Islands[i];
                var player = world.GetPlayerShip(PlayerId)!;
                player.Position = player.PreviousPosition = island.ShoreToward(Vector2.UnitY) + Vector2.UnitY * 6f;
                player.IsAnchored = true;
                RunTicks(world, director.BossCountdownTicks + 1);

                var boss = Boss(world)!;
                Assert.True(world.DistanceToLand(boss.Position) >= 4.9f, $"{island.Name}: spawned {world.DistanceToLand(boss.Position):0.0} from land");
            }
        }
    }

    // ---- Seeds ----------------------------------------------------------------------------------------------

    [Fact]
    public void Runs_AreReproducible_FromTheirSeed()
    {
        static string Play(int seed)
        {
            var (world, _) = CreateRun(players: 2, seed: seed);
            SailTo(world, Next(world).Id);
            var islands = string.Join(";", world.Islands.Select(i => $"{i.Name}@{i.Center}"));
            var pirates = string.Join(";", world.Ships.Where(s => s.Team == Team.Pirates).Select(s => s.Position));
            Raze(world);
            var offers = string.Join("|", world.Players.Values.SelectMany(p => p.CardOffers).Select(o => string.Join(",", o.Cards)));
            return $"{islands} / {pirates} / {offers}";
        }

        Assert.Equal(Play(5), Play(5));
        Assert.NotEqual(Play(5), Play(6));
    }

    [Fact]
    public void WeaponCards_AreOnlyOfferedForWeaponsTheShipCarries()
    {
        var world = new World(new Vector2(100, 100));
        world.SpawnShip(new Vector2(50, 50), 0f, ShipStats.Sloop, PlayerId, Loadouts.Starting(new LongGun()));
        var eligible = CardRewards.Eligible(world, world.Players[PlayerId]);

        Assert.Contains(eligible, c => c.AbilityId == LongGun.AbilityId);
        Assert.All(eligible, c => Assert.True(c.AbilityId is null or LongGun.AbilityId, $"{c.Name} doesn't suit a long gun"));
        var rng = new Random(1);
        for (var level = 1; level <= 8; level++)
            Assert.All(CardRewards.Deal(rng, eligible, OfferSource.Fortress, level).Cards, pick => Assert.Contains(eligible, c => c.Id == pick.Id));
    }
}
