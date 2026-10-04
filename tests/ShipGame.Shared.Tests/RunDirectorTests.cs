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
    /// Open water with <paramref name="fortresses"/> small fortress islands in a row, each with two batteries, a
    /// director, and anchored players who can't be sunk, out of every fort's reach.
    /// </summary>
    private static (World world, RunDirector director, List<Island> fortresses) CreateRun(int players = 1, int fortresses = 2, int seed = 7)
    {
        var world = new World(new Vector2(400, 400)) { Wind = Vector2.Zero };
        var director = new RunDirector(seed);
        world.Director = director;
        var islands = new List<Island>();
        for (var i = 0; i < fortresses; i++)
        {
            var center = new Vector2(40 + 50 * i, 40);
            var island = new Island(i + 1, new[] { center + new Vector2(-4, -4), center + new Vector2(4, -4), center + new Vector2(4, 4), center + new Vector2(-4, 4) },
                level: 1, isFortress: true);
            world.AddIsland(island);
            Fortresses.SpawnFort(world, island, 0f, FortKind.Battery);
            Fortresses.SpawnFort(world, island, MathF.PI, FortKind.Battery);
            islands.Add(island);
        }
        for (var id = 1; id <= players; id++)
        {
            var ship = world.SpawnShip(new Vector2(100 + id * 6, 300), 0f, ShipStats.Sloop, id, Loadouts.Starting(new BroadsideVolley()));
            Invulnerable(ship);
            ship.IsAnchored = true;
        }
        world.Step();
        world.DrainEvents();
        return (world, director, islands);
    }

    private static void Invulnerable(Ship ship) =>
        ship.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 1_000_000f, "test"));

    /// <summary>Sinks every fort on <paramref name="fortresses"/> at once (the game then pauses for cards).</summary>
    private static void Raze(World world, params Island[] fortresses)
    {
        foreach (var fort in world.Ships.Where(s => fortresses.Any(f => f.Id == s.FortIslandId)))
            fort.Health = 0f;
        world.Step();
    }

    /// <summary>Everyone takes the first card of every offer waiting, which ends the pause.</summary>
    private static void ChooseAll(World world)
    {
        while (world.IsPaused)
        {
            foreach (var player in world.Players.Values.Where(p => p.CardOffers.Count > 0))
                world.Enqueue(new ChooseCardCommand(player.PlayerId, player.CardOffers[0].Cards[0].Id));
            world.Step();
        }
    }

    /// <summary>Takes the fortresses and has everyone choose, so the run carries on.</summary>
    private static void Take(World world, params Island[] fortresses)
    {
        Raze(world, fortresses);
        ChooseAll(world);
    }

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    private static Ship? Boss(World world) => world.Ships.SingleOrDefault(s => s.IsBoss);

    // ---- Fortresses and cards ---------------------------------------------------------------------------

    [Fact]
    public void TakingAFortress_OffersEveryPlayerThreeDifferentCards()
    {
        var (world, director, fortresses) = CreateRun(players: 2);

        Raze(world, fortresses[0]);

        Assert.False(world.IsHeld(fortresses[0]));
        Assert.True(world.IsHeld(fortresses[1]));
        Assert.Equal(1, director.FortressesTaken);
        var events = world.DrainEvents();
        Assert.Equal(fortresses[0].Id, Assert.Single(events.OfType<FortressTaken>()).IslandId);
        Assert.Equal(new[] { 1, 2 }, events.OfType<CardsOffered>().Select(e => e.PlayerId).Order());
        foreach (var player in world.Players.Values)
        {
            var offer = Assert.Single(player.CardOffers);
            Assert.Equal((OfferSource.Fortress, 1), (offer.Source, offer.Level));
            Assert.Equal(CardRewards.OfferSize, offer.Cards.Count);
            Assert.Equal(offer.Cards.Count, offer.Cards.Select(c => c.Id).Distinct().Count());
            Assert.All(offer.Cards, c => Assert.NotNull(CardCatalog.Find(c.Id)));
        }
    }

    [Fact]
    public void AFortress_HoldsWhileAnyFortStands()
    {
        var (world, director, fortresses) = CreateRun();
        world.Ships.First(s => s.FortIslandId == fortresses[0].Id).Health = 0f;
        RunTicks(world, 2);

        Assert.True(world.IsHeld(fortresses[0]));
        Assert.Equal(0, director.FortressesTaken);
        Assert.Empty(world.Players[PlayerId].CardOffers);
    }

    [Fact]
    public void Offers_AreDrawnForEachPlayer_AndQueueUp()
    {
        var (world, _, fortresses) = CreateRun(players: 2, fortresses: 4, seed: 3);
        Raze(world, fortresses.ToArray());

        var one = world.Players[1].CardOffers;
        var two = world.Players[2].CardOffers;
        Assert.Equal(4, one.Count);
        Assert.Equal(4, two.Count);
        Assert.False(one.Zip(two).All(pair => pair.First.Cards.SequenceEqual(pair.Second.Cards)), "both players were dealt the same hands");
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

    // ---- Bosses -----------------------------------------------------------------------------------------

    [Fact]
    public void OneFortress_CallsNoBoss()
    {
        var (world, director, fortresses) = CreateRun();
        Take(world, fortresses[0]);
        RunTicks(world, RunDirector.BossWarningTicks + 10);

        Assert.Equal(0, director.BossCountdownTicks);
        Assert.Null(Boss(world));
    }

    [Fact]
    public void TwoFortresses_CallABoss_AfterAWarning_NearAPlayer()
    {
        var (world, director, fortresses) = CreateRun(players: 3);
        Take(world, fortresses[0]);
        Take(world, fortresses[1]);
        Assert.InRange(director.BossCountdownTicks, RunDirector.BossWarningTicks - 1, RunDirector.BossWarningTicks);
        Assert.Equal(2, director.Status.FortressesForNextBoss);

        RunTicks(world, director.BossCountdownTicks - 2);
        Assert.Null(Boss(world));
        RunTicks(world, 2);

        var boss = Boss(world)!;
        Assert.NotNull(boss);
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
        Assert.Equal(4, director.Status.FortressesForNextBoss);
    }

    [Fact]
    public void Bosses_ComeOneAtATime_EachStrongerThanTheLast()
    {
        var (world, director, fortresses) = CreateRun(fortresses: 4);
        Take(world, fortresses.ToArray());
        RunTicks(world, RunDirector.BossWarningTicks + 1);
        var first = Boss(world)!;

        // Four fortresses down, but the second boss waits for the first to sink.
        RunTicks(world, RunDirector.BossWarningTicks * 2);
        Assert.Same(first, Boss(world));
        Assert.Equal(0, director.BossCountdownTicks);

        first.Health = 0f;
        world.Step();
        Assert.Equal(1, director.BossesSunk);
        Assert.False(world.IsRunOver);
        ChooseAll(world); // its spoils
        RunTicks(world, RunDirector.BossWarningTicks + 1);
        var second = Boss(world)!;

        Assert.NotSame(first, second);
        Assert.True(second.Level > first.Level);
        Assert.True(second.Stats.MaxHealth > first.Stats.MaxHealth);
        Assert.True(second.HasAbility(Mortar.AbilityId) && !first.HasAbility(Mortar.AbilityId));
    }

    [Fact]
    public void Bosses_PayOutPrismatics_StrongerForEach_ButNotTheLast()
    {
        var (world, director, _) = CreateRun(players: 2);
        foreach (var round in new[] { 1, 2 })
        {
            director.Restore(new RunStatus(FortressesTaken: 2 * round, BossesSunk: round - 1, BossCountdownTicks: 1, BossAfloat: false));
            world.Step();
            Boss(world)!.Health = 0f;
            world.Step();

            Assert.Equal(round, director.BossesSunk);
            foreach (var player in world.Players.Values)
            {
                var offer = Assert.Single(player.CardOffers);
                Assert.Equal((OfferSource.Boss, CardRewards.BossDropLevel(round)), (offer.Source, offer.Level));
                Assert.All(offer.Cards, c => Assert.Equal((CardTier.Prismatic, offer.Level), (c.Definition.Tier, c.Level)));
            }
            Assert.Equal(2 * round, director.FortressesTaken); // they don't count toward the next boss
            ChooseAll(world);
        }
        Assert.True(CardRewards.BossDropLevel(2) > CardRewards.BossDropLevel(1));
    }

    [Fact]
    public void SinkingTheLastBoss_WinsTheRun()
    {
        var (world, director, _) = CreateRun();
        director.Restore(new RunStatus(FortressesTaken: 6, BossesSunk: 2, BossCountdownTicks: 0, BossAfloat: false));
        RunTicks(world, RunDirector.BossWarningTicks + 1);
        var boss = Boss(world)!;
        Assert.Equal(RunDirector.BossLevel(3), boss.Level);
        Assert.All(new[] { BroadsideVolley.AbilityId, Mortar.AbilityId, LongGun.AbilityId }, id => Assert.True(boss.HasAbility(id)));
        world.DrainEvents();

        boss.Health = 0f;
        boss.LastHitByShipId = world.GetPlayerShip(PlayerId)!.Id;
        world.Step();

        Assert.True(world.IsRunOver);
        Assert.True(world.IsVictory);
        Assert.Equal(RunDirector.BossCount, director.BossesSunk);
        Assert.True(Assert.Single(world.DrainEvents().OfType<RunEnded>()).Victory);
        Assert.Empty(world.Players[PlayerId].CardOffers); // nothing to choose: it's won
        Assert.Equal(KillRewards.GoldFor(boss), world.Players[PlayerId].Gold);
    }

    [Fact]
    public void Bosses_AreSturdierForBiggerCrews()
    {
        static float BossHealth(int players)
        {
            var (world, director, _) = CreateRun(players);
            director.Restore(new RunStatus(FortressesTaken: 2, BossesSunk: 0, BossCountdownTicks: 0, BossAfloat: false));
            RunTicks(world, RunDirector.BossWarningTicks + 1);
            return Boss(world)!.Stats.MaxHealth;
        }

        Assert.Equal(BossHealth(1) * (1f + 3 * RunDirector.BossHealthPerExtraPlayer), BossHealth(4), 2);
    }

    [Fact]
    public void Runs_AreReproducible_FromTheirSeed()
    {
        static string Play(int seed)
        {
            var (world, _, fortresses) = CreateRun(players: 2, seed: seed);
            Raze(world, fortresses.ToArray());
            var offers = string.Join("|", world.Players.Values.SelectMany(p => p.CardOffers).Select(o => string.Join(",", o.Cards)));
            ChooseAll(world);
            RunTicks(world, RunDirector.BossWarningTicks + 1);
            return $"{offers} @ {Boss(world)!.Position}";
        }

        Assert.Equal(Play(5), Play(5));
        Assert.NotEqual(Play(5), Play(6));
    }

    [Fact]
    public void Bosses_NeverSpawnOnOrAgainstLand()
    {
        // Players parked off every island of the real map in turn: the boss still finds open water.
        foreach (var island in Archipelago.CreateIslands())
        {
            var world = Runs.CreateMap();
            var director = new RunDirector(island.Id);
            world.Director = director;
            var player = world.SpawnShip(island.ShoreToward(Vector2.UnitY) + Vector2.UnitY * 6f, 0f, ShipStats.Sloop, PlayerId);
            Invulnerable(player);
            player.IsAnchored = true;
            director.Restore(new RunStatus(FortressesTaken: 2, BossesSunk: 0, BossCountdownTicks: 1, BossAfloat: false));

            world.Step();

            var boss = Boss(world)!;
            Assert.True(world.DistanceToLand(boss.Position) >= 4.9f, $"{island.Name}: spawned {world.DistanceToLand(boss.Position):0.0} from land");
        }
    }
}
