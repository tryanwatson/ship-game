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

/// <summary>The long map north: its seas and their levels, the pirates placed on it, and what levels change.</summary>
public class NorthernSeasTests
{
    private const int PlayerId = 1;

    private static readonly IReadOnlyList<Island> Islands = Archipelago.CreateIslands();

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    // ---- The map ----------------------------------------------------------------------------------------

    [Fact]
    public void Seas_TileTheMap_FromSouthToNorth_GettingHarder()
    {
        var seas = Archipelago.Seas;
        Assert.Equal(Archipelago.Size.Y, seas[0].South);
        Assert.Equal(0f, seas[^1].North);
        for (var i = 1; i < seas.Count; i++)
        {
            Assert.Equal(seas[i - 1].North, seas[i].South);
            Assert.True(seas[i].Level > seas[i - 1].Level, $"{seas[i].Name} should be harder than {seas[i - 1].Name}");
        }
        Assert.Equal(seas[0], Archipelago.SeaAt(Archipelago.Start));
        Assert.Equal(seas[^1], Archipelago.SeaAt(Archipelago.BossPosition));
    }

    [Fact]
    public void Islands_LieInsideTheMap_WithRoomToSailBetweenThem()
    {
        foreach (var island in Islands)
        {
            foreach (var point in island.Outline)
            {
                Assert.InRange(point.X, 6f, Archipelago.Size.X - 6f);
                Assert.InRange(point.Y, 6f, Archipelago.Size.Y - 6f);
            }
        }
        for (var i = 0; i < Islands.Count; i++)
        {
            for (var j = i + 1; j < Islands.Count; j++)
            {
                var gap = Gap(Islands[i], Islands[j]);
                Assert.True(gap >= 8f, $"{Islands[i].Name} and {Islands[j].Name} are only {gap:0.0} apart");
            }
        }
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
    public void EverySeaButTheLast_HasOneShipyard()
    {
        foreach (var sea in Archipelago.Seas)
        {
            var shipyards = Islands.Count(i => i.HasShipyard && sea.Contains(i.Center.Y));
            Assert.Equal(sea == Archipelago.Seas[^1] ? 0 : 1, shipyards);
        }
    }

    [Fact]
    public void Islands_TakeTheLevelOfTheirSea_AndPlunderForMoreFurtherNorth()
    {
        foreach (var island in Islands)
        {
            Assert.Equal(Archipelago.LevelAt(island.Center), island.Level);
            Assert.Equal(PirateLevels.PlunderGold(island.Level), island.PlunderGold);
        }
        Assert.Equal(Island.DefaultPlunderGold, PirateLevels.PlunderGold(1));
        Assert.Equal(Island.DefaultPlunderGold * 5, PirateLevels.PlunderGold(5));
    }

    [Fact]
    public void Camps_AreAtSea_AtAboutTheirWatersLevel_AndClearOfTheStart()
    {
        var world = Runs.CreateMap();
        foreach (var camp in Archipelago.Camps)
        {
            Assert.InRange(camp.Position.X, 4f, Archipelago.Size.X - 4f);
            Assert.InRange(camp.Position.Y, 4f, Archipelago.Size.Y - 4f);
            Assert.True(world.DistanceToLand(camp.Position) >= 2.5f, $"camp at {camp.Position} is on land");
            Assert.InRange(camp.Level - Archipelago.LevelAt(camp.Position), 0, 1);
            Assert.True(Vector2.Distance(camp.Position, Archipelago.Start) > HunterBehavior.AggroRange + 20f, $"camp at {camp.Position} guards the start");
        }
        Assert.True(world.DistanceToLand(Archipelago.Start) >= 10f);
        Assert.True(world.DistanceToLand(Archipelago.BossPosition) >= 4f);
    }

    // ---- Putting pirates to sea -------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void Populate_PutsEveryCampAndTheFlagshipToSea_ClearOfLand(int players)
    {
        var crew = Enumerable.Range(1, players).Select(id => (id, (Ability)new BroadsideVolley())).ToList();
        var world = Runs.Create(seed: 1, crew);

        var pirates = world.Ships.Where(s => s.Team == Team.Pirates).ToList();
        Assert.Equal(Archipelago.Camps.Sum(c => PirateCamps.CampSize(c.Count, players)) + 1, pirates.Count);
        foreach (var pirate in pirates)
        {
            Assert.True(world.DistanceToLand(pirate.Position) >= 2.4f, $"a pirate at {pirate.Position} is on land");
            Assert.Equal(NpcStance.Patrolling, pirate.Stance);
            Assert.InRange(pirate.Level, 1, Archipelago.BossLevel);
            Assert.Equal(pirate.Stats.MaxHealth, pirate.Health);
        }

        Assert.All(pirates.Where(p => !p.IsBoss), p => Assert.Equal(ShipStats.PirateSloop, p.BaseStats));
        Assert.Equal(ShipStats.Sloop.MaxHealth / 2f, ShipStats.PirateSloop.MaxHealth);
        var flagship = Assert.Single(pirates, p => p.IsBoss);
        Assert.Equal(Archipelago.BossLevel, flagship.Level);
        Assert.Equal(ShipStats.Flagship, flagship.BaseStats);
    }

    [Fact]
    public void Run_StartsTheCrewAbreastAtTheSouthernEdge_FacingNorth()
    {
        var crew = Enumerable.Range(1, 3).Select(id => (id, (Ability)new LongGun())).ToList();
        var world = Runs.Create(seed: 1, crew);

        var ships = world.Ships.Where(s => s.OwnerPlayerId is not null).OrderBy(s => s.Position.X).ToList();
        Assert.Equal(3, ships.Count);
        Assert.All(ships, s => Assert.Equal(Archipelago.Start.Y, s.Position.Y));
        Assert.All(ships, s => Assert.Equal(Archipelago.StartHeading, s.Heading));
        Assert.All(ships, s => Assert.Equal(LongGun.AbilityId, s.GetAbility(AbilitySlot.One)!.Definition.Id));
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
    public void Kills_PayMoreForHigherLevels()
    {
        var world = new World(new Vector2(96, 96)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(20, 20), 0f, ShipStats.Sloop, PlayerId);
        var pirate = world.SpawnShip(new Vector2(60, 60), 0f, ShipStats.Sloop, abilities: Loadouts.Pirate);
        PirateLevels.Apply(pirate, 4);

        pirate.Health = 0f;
        pirate.LastHitByShipId = player.Id;
        world.Step();

        Assert.Equal(4 * KillRewards.Gold, world.Players[PlayerId].Gold);
    }

    // ---- The end ----------------------------------------------------------------------------------------

    [Fact]
    public void SinkingTheFlagship_WinsTheRun()
    {
        var world = new World(new Vector2(96, 96)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(20, 20), 0f, ShipStats.Sloop, PlayerId);
        var flagship = world.SpawnShip(new Vector2(60, 60), 0f, ShipStats.Flagship, abilities: Loadouts.Pirate);
        flagship.IsBoss = true;
        world.DrainEvents();

        flagship.Health = 0f;
        flagship.LastHitByShipId = player.Id;
        world.Step();

        Assert.True(world.IsRunOver);
        Assert.True(world.IsVictory);
        Assert.True(Assert.Single(world.DrainEvents().OfType<RunEnded>()).Victory);
    }

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
    public void Shipyards_StockMoreLevels_FurtherNorth()
    {
        var hull = UpgradeCatalog.Find("hull")!;
        var ports = Islands.Where(i => i.HasShipyard).OrderByDescending(i => i.Center.Y).ToList();
        Assert.Equal(Shipyards.BaseStockedLevels + 1, Shipyards.StockedLevels(ports[0], hull));
        Assert.Equal(hull.MaxLevel, Shipyards.StockedLevels(ports[^1], hull));
        for (var i = 1; i < ports.Count; i++)
            Assert.True(Shipyards.StockedLevels(ports[i], hull) > Shipyards.StockedLevels(ports[i - 1], hull));
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
    public void Contracts_RunNorthOrAcross_WithinReach()
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
                Assert.True(destination.Center.Y - post.Center.Y <= Contracts.MaxSouthward, $"{post.Name} sends cargo back south to {destination.Name}");
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
