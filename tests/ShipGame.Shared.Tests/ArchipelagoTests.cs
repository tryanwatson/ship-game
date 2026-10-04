using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Trading;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Tests;

/// <summary>The square map: its rings of sea and their levels, its islands and fortresses, the pirates placed on it, and what levels change.</summary>
public class ArchipelagoTests
{
    private const int PlayerId = 1;

    private static readonly IReadOnlyList<Island> Islands = Archipelago.CreateIslands();

    // ---- The map ----------------------------------------------------------------------------------------

    [Fact]
    public void Map_IsSquare_AndStartsTheCrewInTheMiddle()
    {
        Assert.Equal(Archipelago.Size.X, Archipelago.Size.Y);
        Assert.Equal(Archipelago.Size / 2f, Archipelago.Start);
    }

    [Fact]
    public void Seas_RingOutwardFromTheStart_GettingHarder()
    {
        var seas = Archipelago.Seas;
        Assert.Equal(0f, seas[0].InnerRadius);
        Assert.True(float.IsPositiveInfinity(seas[^1].OuterRadius));
        for (var i = 1; i < seas.Count; i++)
        {
            Assert.Equal(seas[i - 1].OuterRadius, seas[i].InnerRadius);
            Assert.True(seas[i].Level > seas[i - 1].Level, $"{seas[i].Name} should be harder than {seas[i - 1].Name}");
        }
        Assert.Equal(seas[0], Archipelago.SeaAt(Archipelago.Start));
        Assert.Equal(seas[^1], Archipelago.SeaAt(Vector2.Zero)); // the corners are the furthest out
        Assert.True(Archipelago.LevelAt(new Vector2(Archipelago.Start.X, 20f)) > Archipelago.LevelAt(Archipelago.Start + new Vector2(0f, 100f)));
    }

    [Fact]
    public void Islands_AreLarge_InsideTheMap_AndFarApart()
    {
        foreach (var island in Islands)
        {
            Assert.InRange(island.Area, 150f, 500f);
            foreach (var point in island.Outline)
            {
                Assert.InRange(point.X, 20f, Archipelago.Size.X - 20f);
                Assert.InRange(point.Y, 20f, Archipelago.Size.Y - 20f);
            }
        }
        for (var i = 0; i < Islands.Count; i++)
        {
            for (var j = i + 1; j < Islands.Count; j++)
            {
                var gap = Gap(Islands[i], Islands[j]);
                Assert.True(gap >= 60f, $"{Islands[i].Name} and {Islands[j].Name} are only {gap:0.0} apart");
            }
        }
        Assert.Equal(Islands.Count, Islands.Select(i => i.Name).Distinct().Count());
    }

    private static float Gap(Island a, Island b)
    {
        var gap = float.MaxValue;
        foreach (var point in a.Outline)
            gap = MathF.Min(gap, b.DistanceTo(point));
        foreach (var point in b.Outline)
            gap = MathF.Min(gap, a.DistanceTo(point));
        return gap;
    }

    [Fact]
    public void TheStart_IsOpenWater_WithAShipyardNearby()
    {
        var world = Runs.CreateMap();
        Assert.True(world.DistanceToLand(Archipelago.Start) >= 40f);
        var nearest = Islands.MinBy(i => i.DistanceTo(Archipelago.Start))!;
        Assert.True(nearest.HasShipyard, $"the nearest island to the start is {nearest.Name}");
    }

    [Fact]
    public void Fortresses_AreEnoughForEveryBoss_AndToughenFurtherOut()
    {
        var fortresses = Islands.Where(i => i.IsFortress).ToList();
        Assert.True(fortresses.Count >= RunDirector.FortressesPerBoss * RunDirector.BossCount * 2,
            $"only {fortresses.Count} fortresses: the crew should have a choice");
        Assert.True(fortresses.Count(f => f.Level == 1) >= RunDirector.FortressesPerBoss, "the first boss should be reachable from the shallows");
        Assert.All(fortresses, f => Assert.False(f.HasShipyard));

        // Each a level or two above its waters at most, and on average harder the further out.
        Assert.All(fortresses, f => Assert.InRange(f.Level - Archipelago.LevelAt(f.Center), 0, 1));
        var averages = fortresses.GroupBy(f => Archipelago.LevelAt(f.Center)).OrderBy(g => g.Key).Select(g => g.Average(f => f.Level)).ToList();
        for (var i = 1; i < averages.Count; i++)
            Assert.True(averages[i] > averages[i - 1]);
    }

    [Fact]
    public void Shipyards_AreSpreadOutward()
    {
        var shipyards = Islands.Where(i => i.HasShipyard).ToList();
        Assert.True(shipyards.Count >= 4);
        Assert.True(shipyards.Select(s => Archipelago.LevelAt(s.Center)).Distinct().Count() >= 4);
    }

    [Fact]
    public void Islands_TakeTheLevelOfTheirWaters_AndPlunderForMoreFurtherOut()
    {
        foreach (var island in Islands)
        {
            if (!island.IsFortress)
                Assert.Equal(Archipelago.LevelAt(island.Center), island.Level);
            var multiplier = island.IsFortress ? Archipelago.FortressPlunderMultiplier : 1;
            Assert.Equal(PirateLevels.PlunderGold(island.Level) * multiplier, island.PlunderGold);
        }
        Assert.Equal(Island.DefaultPlunderGold, PirateLevels.PlunderGold(1));
        Assert.Equal(Island.DefaultPlunderGold * 5, PirateLevels.PlunderGold(5));
    }

    [Fact]
    public void Camps_AreAtSea_AtTheirWatersLevel_AndClearOfTheStart()
    {
        var world = Runs.CreateMap();
        foreach (var camp in Archipelago.Camps)
        {
            Assert.InRange(camp.Position.X, 20f, Archipelago.Size.X - 20f);
            Assert.InRange(camp.Position.Y, 20f, Archipelago.Size.Y - 20f);
            Assert.True(world.DistanceToLand(camp.Position) >= 20f, $"camp at {camp.Position} is hard against land");
            Assert.Equal(Archipelago.LevelAt(camp.Position), camp.Level);
            Assert.True(Vector2.Distance(camp.Position, Archipelago.Start) >= PirateCamps.StartBerth + HunterBehavior.AggroRange,
                $"camp at {camp.Position} guards the start");
        }
    }

    // ---- Putting pirates to sea -------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void Populate_MansEveryFortress_AndSendsOutEveryPack_ClearOfLand(int players)
    {
        var crew = Enumerable.Range(1, players).Select(id => (id, $"SAILOR {id}")).ToList();
        var world = Runs.Create(seed: 1, crew);

        var pirates = world.Ships.Where(s => s.Team == Team.Pirates).ToList();
        var forts = pirates.Where(p => p.IsFort).ToList();
        var ships = pirates.Where(p => !p.IsFort).ToList();
        var fortresses = world.Islands.Where(i => i.IsFortress).ToList();

        Assert.Equal(fortresses.Sum(f => Fortresses.Forts(f.Level)), forts.Count);
        Assert.Equal(fortresses.Sum(f => PirateCamps.CampSize(Fortresses.GuardShips(f.Level), players))
                     + Archipelago.Camps.Sum(c => PirateCamps.CampSize(c.Count, players)), ships.Count);
        Assert.DoesNotContain(pirates, p => p.IsBoss); // bosses come later
        foreach (var pirate in ships)
        {
            Assert.True(world.DistanceToLand(pirate.Position) >= 2.4f, $"a pirate at {pirate.Position} is on land");
            Assert.Equal(NpcStance.Patrolling, pirate.Stance);
            Assert.Equal(pirate.Stats.MaxHealth, pirate.Health);
            Assert.Equal(ShipStats.PirateSloop, pirate.BaseStats);
        }
        Assert.Equal(ShipStats.Sloop.MaxHealth / 2f, ShipStats.PirateSloop.MaxHealth);
        foreach (var fort in forts)
        {
            var island = world.FindIsland(fort.FortIslandId!.Value)!;
            Assert.Equal(island.Level, fort.Level);
            Assert.Equal(0f, island.DistanceTo(fort.Position)); // on land...
            Assert.True(Vector2.Distance(island.ShoreToward(Vector2.Normalize(fort.Position - island.Center)), fort.Position) < 1f); // ...on the shore
        }
    }

    [Fact]
    public void Run_StartsTheCrewAbreastInTheMiddle_FacingNorth()
    {
        var crew = Enumerable.Range(1, 3).Select(id => (id, $"SAILOR {id}")).ToList();
        var world = Runs.Create(seed: 1, crew);

        var ships = world.Ships.Where(s => s.OwnerPlayerId is not null).OrderBy(s => s.Position.X).ToList();
        Assert.Equal(3, ships.Count);
        Assert.All(ships, s => Assert.Equal(Archipelago.Start.Y, s.Position.Y));
        Assert.All(ships, s => Assert.Equal(Archipelago.StartHeading, s.Heading));
        Assert.All(ships, s => Assert.All(s.Abilities, Assert.Null)); // the weapon's chosen once the run opens
        Assert.Equal(Archipelago.Start.X, ships[1].Position.X, 3);
        Assert.NotNull(world.Director);
        Assert.Equal(Archipelago.Seas[0], Archipelago.SeaAt(ships[0].Position));
    }

    [Theory]
    [InlineData(2, 1, 2)]
    [InlineData(2, 3, 4)]
    [InlineData(3, 12, 6)] // held at the cap
    [InlineData(1, 12, 6)]
    public void Camps_GrowWithTheCrew_UpToACap(int count, int players, int expected)
    {
        Assert.Equal(expected, PirateCamps.CampSize(count, players));
    }

    // ---- Levels -----------------------------------------------------------------------------------------

    [Fact]
    public void Levels_ToughenPirates_AndReapplyingReplacesThem()
    {
        var world = new World(new Vector2(96, 96));
        var pirate = world.SpawnShip(new Vector2(40, 40), 0f, ShipStats.Sloop, abilities: Loadouts.Pirate);

        PirateLevels.Apply(pirate, 3);
        Assert.Equal(3, pirate.Level);
        Assert.Equal(ShipStats.Sloop.MaxHealth * (1f + 2 * PirateLevels.HealthPerLevel), pirate.Stats.MaxHealth, 3);
        Assert.Equal(pirate.Stats.MaxHealth, pirate.Health);
        Assert.Equal(1f + 2 * PirateLevels.DamagePerLevel, pirate.Stats.WeaponDamage, 4);
        Assert.Equal(1f + 2 * PirateLevels.CooldownSpeedPerLevel, pirate.Stats.CooldownSpeed, 4);
        Assert.Equal(ShipStats.Sloop.MaxSpeed * (1f + 2 * PirateLevels.SpeedPerLevel), pirate.Stats.MaxSpeed, 4);

        PirateLevels.Apply(pirate, 1);
        Assert.Equal(ShipStats.Sloop, pirate.Stats);
    }

    [Fact]
    public void Kills_PayMoreForHigherLevels_AndMoreForFortsAndBosses()
    {
        var world = new World(new Vector2(96, 96)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(20, 20), 0f, ShipStats.Sloop, PlayerId);
        var pirate = world.SpawnShip(new Vector2(60, 60), 0f, ShipStats.Sloop, abilities: Loadouts.Pirate);
        PirateLevels.Apply(pirate, 4);

        pirate.Health = 0f;
        pirate.LastHitByShipId = player.Id;
        world.Step();
        Assert.Equal(4 * KillRewards.Gold, world.Players[PlayerId].Gold);

        var fort = world.SpawnShip(new Vector2(60, 60), 0f, ShipStats.Fort, abilities: Loadouts.Starting(new LongGun()));
        fort.FortIslandId = 9;
        PirateLevels.Apply(fort, 2);
        Assert.Equal(2 * KillRewards.Gold * KillRewards.FortMultiplier, KillRewards.GoldFor(fort));
        var boss = world.SpawnShip(new Vector2(60, 60), 0f, ShipStats.Flagship);
        boss.IsBoss = true;
        PirateLevels.Apply(boss, 3);
        Assert.Equal(3 * KillRewards.Gold * KillRewards.BossMultiplier, KillRewards.GoldFor(boss));
    }

    // ---- The end ----------------------------------------------------------------------------------------

    [Fact]
    public void SinkingOtherPirates_DoesNotEndTheRun()
    {
        var world = new World(new Vector2(96, 96)) { Wind = Vector2.Zero };
        world.SpawnShip(new Vector2(20, 20), 0f, ShipStats.Sloop, PlayerId);
        var pirate = world.SpawnShip(new Vector2(60, 60), 0f, ShipStats.Sloop);

        pirate.Health = 0f;
        world.Step();

        Assert.False(world.IsRunOver);
    }

    [Fact]
    public void AWipe_IsNoVictory()
    {
        var world = new World(new Vector2(96, 96)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(20, 20), 0f, ShipStats.Sloop, PlayerId);

        player.Health = 0f;
        world.Step();

        Assert.True(world.IsRunOver);
        Assert.False(world.IsVictory);
    }

    // ---- Ports and trade --------------------------------------------------------------------------------

    [Fact]
    public void Shipyards_StockMoreLevels_FurtherOut()
    {
        var hull = UpgradeCatalog.Find("hull")!;
        var ports = Islands.Where(i => i.HasShipyard).OrderBy(i => Archipelago.DistanceFromStart(i.Center)).ToList();
        Assert.Equal(Shipyards.BaseStockedLevels + 1, Shipyards.StockedLevels(ports[0], hull));
        for (var i = 1; i < ports.Count; i++)
            Assert.True(Shipyards.StockedLevels(ports[i], hull) >= Shipyards.StockedLevels(ports[i - 1], hull));
        Assert.True(Shipyards.StockedLevels(ports[^1], hull) > Shipyards.StockedLevels(ports[0], hull));
    }

    [Fact]
    public void Upgrades_PastWhatAYardStocks_AreRefused()
    {
        var world = new World(new Vector2(128, 128)) { Wind = Vector2.Zero };
        var port = new Island(1, new[] { new Vector2(40, 26), new Vector2(48, 26), new Vector2(48, 34), new Vector2(40, 34) },
            hasShipyard: true, level: 1);
        world.AddIsland(port);
        var ship = world.SpawnShip(new Vector2(37, 30), 0f, ShipStats.Sloop, PlayerId);
        world.Players[PlayerId].Gold = 10_000;
        ship.IsAnchored = true;
        world.Step();

        var hull = UpgradeCatalog.Find("hull")!;
        for (var i = 0; i < Shipyards.StockedLevels(port, hull); i++)
            world.Enqueue(new PurchaseUpgradeCommand(PlayerId, hull.Id));
        world.Step();
        world.DrainEvents();
        Assert.Equal(Shipyards.StockedLevels(port, hull), Shipyards.Level(ship, hull));

        world.Enqueue(new PurchaseUpgradeCommand(PlayerId, hull.Id));
        world.Step();

        Assert.Equal(Shipyards.StockedLevels(port, hull), Shipyards.Level(ship, hull));
        Assert.Equal(RejectionReason.NotStockedHere, Assert.Single(world.DrainEvents().OfType<CommandRejected>()).Reason);
    }

    [Fact]
    public void Contracts_RunOutwardOrAcross_WithinReach()
    {
        var world = Runs.CreateMap();
        Contracts.OpenMarkets(world, seed: 3);

        foreach (var post in world.Islands.Where(i => i.HasShipyard))
        {
            var offers = world.Trade.OffersAt(post.Id);
            Assert.NotEmpty(offers);
            foreach (var offer in offers)
            {
                var destination = world.FindIsland(offer.DestinationIslandId)!;
                Assert.True(Contracts.Inward(world, post, destination) <= Contracts.MaxInward,
                    $"{post.Name} sends cargo back inward to {destination.Name}");
                Assert.True(Contracts.RouteDistance(post, destination) <= Contracts.MaxRouteDistance);
                Assert.Equal(Contracts.PayoutFor(offer.Cost, offer.CargoUnits, Contracts.RouteDistance(post, destination), destination.Level),
                    offer.Payout);
            }
        }
    }

    [Fact]
    public void Contracts_PayADangerPremium_ForRougherWaters()
    {
        var calm = Contracts.PayoutFor(20, 10, 100f, destinationLevel: 1);
        var rough = Contracts.PayoutFor(20, 10, 100f, destinationLevel: 5);
        var hauling = 10 * 100f * Contracts.PayPerUnitTile;

        Assert.Equal(Contracts.RoundGold(20 * (1 + Contracts.CapitalReturn) + hauling), calm);
        Assert.Equal(Contracts.RoundGold(20 * (1 + Contracts.CapitalReturn) + hauling * (1 + 4 * Contracts.DangerPremiumPerLevel)), rough);
    }
}
