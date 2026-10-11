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

/// <summary>
/// The rules from the 2026-10-10 balance pass: bigger crews meet more forts on bigger islands, bosses fight in phases
/// and call escorts, besieged fortresses send for relief, sinking costs more each time, lone sailors get a lifeboat an
/// act, a hull mends itself only so far, crewmates' blasts are softened, and ports sell hands of cards.
/// </summary>
public class BalanceRulesTests
{
    /// <summary>A world with a crew of <paramref name="players"/> sailed straight into <paramref name="node"/> by a director.</summary>
    private static (World World, RunDirector Director) At(ChartNode node, int players = 1, int seed = 3)
    {
        var world = new World(Regions.StartSize) { Wind = Vector2.Zero };
        var director = new RunDirector(seed) { Chart = new SeaChart(new[] { Start, node }) };
        world.Director = director;
        for (var id = 1; id <= players; id++)
        {
            world.GetOrAddPlayer(id);
            world.SpawnShip(world.WorldSize / 2f, 0f, ShipStats.Sloop, id, Loadouts.Starting(WeaponCatalog.LongGun.Ability));
        }
        Runs.GrantLifeboats(world);
        director.SailTo(world, node.Id);
        world.DrainEvents();
        return (world, director);
    }

    private static readonly ChartNode Start = new(0, 1, -1, SeaChart.MiddleLane, NodeKind.Start, 1, Difficulty.Calm, new[] { 1 });

    private static ChartNode Fortress(int level, int act = 2, Difficulty difficulty = Difficulty.Calm) =>
        new(1, act, 1, SeaChart.MiddleLane, NodeKind.Fortress, level, difficulty, Array.Empty<int>());

    private static ChartNode BossStop(int round) =>
        new(1, round, SeaChart.BossRow, SeaChart.MiddleLane, NodeKind.Boss, SeaChart.BossLevel(round), Difficulty.Dire, Array.Empty<int>());

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    // ---- Crew size ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void BiggerCrews_MeetMoreForts_OnABiggerIsland_WithRoomForThemAll(int players)
    {
        var (world, _) = At(Fortress(level: 8, act: 3), players);
        var island = Assert.Single(world.Islands, i => i.IsFortress);
        var forts = world.Ships.Where(s => s.FortIslandId == island.Id).ToList();

        Assert.Equal(Fortresses.Forts(8, players), forts.Count);
        Assert.Equal(Fortresses.MortarTowers(8, players), forts.Count(f => Fortresses.KindOf(f) == FortKind.MortarTower));
        Assert.Equal(Regions.FortressArea(8, players), island.Area, 0);
        // The sea grows round the island, and there's still open water between it and the crew's entry.
        Assert.True(world.WorldSize.X >= 2f * island.BoundingRadius + Regions.FortressSeaRoom - 0.01f);
        Assert.True(world.RegionEntry.Y - (island.Center.Y + island.BoundingRadius) > 40f);
        // Neighbouring forts stand apart along the shore.
        var nearest = forts.Min(f => forts.Where(o => o != f).Min(o => Vector2.Distance(o.Position, f.Position)));
        Assert.True(nearest > 4f, $"forts {nearest:0.0} apart");
    }

    [Fact]
    public void TheCrewsForts_WearThroughToTheCrewsShare_AndGuardsSplitIntoPacks()
    {
        Assert.Equal(1f, Fortresses.CrewScale(1));
        Assert.Equal(Fortresses.CrewScale(8), Fortresses.CountScale(8) * Fortresses.FortHealthScale(8), 3);
        Assert.True(Fortresses.Forts(8, 8) > 3 * Fortresses.Forts(8));

        var (world, _) = At(Fortress(level: 8, act: 3), players: 8);
        var guards = world.Ships.Where(s => s.Team == Team.Pirates && !s.IsFort).ToList();
        Assert.Equal(PirateCamps.CampSize(Fortresses.GuardShips(8), 8), guards.Count);
        Assert.True(guards.Count > 6); // no longer held at six
    }

    [Fact]
    public void TheFirstRowsRoughFortress_IsHarderForGuards_NotALevel()
    {
        var calm = At(new ChartNode(1, 1, 0, 0, NodeKind.Fortress, 1, Difficulty.Calm, Array.Empty<int>())).World;
        var rough = At(new ChartNode(1, 1, 0, 1, NodeKind.Fortress, 1, Difficulty.Rough, Array.Empty<int>())).World;
        int Guards(World w) => w.Ships.Count(s => s.Team == Team.Pirates && !s.IsFort);
        Assert.Equal(Guards(calm) + SeaChart.GuardsPerStep, Guards(rough));
    }

    // ---- Relief fleets -----------------------------------------------------------------------------------

    [Fact]
    public void ABesiegedFortress_SendsForRelief_AsItsFortsFall()
    {
        var (world, director) = At(Fortress(level: SeaChart.FortressLevel(3, 2, Difficulty.Dire), act: 3));
        foreach (var ship in world.Ships.Where(s => s.OwnerPlayerId is not null))
            ship.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 1_000_000f, "test"));
        var forts = world.Ships.Where(s => s.IsFort).ToList();
        var before = world.Ships.Count(s => s.Team == Team.Pirates && !s.IsFort);

        // A third down: the first fleet.
        foreach (var fort in forts.Take((forts.Count + 2) / 3))
            fort.Health = 0f;
        world.Step();
        var relief = Assert.Single(world.DrainEvents().OfType<ReliefFleetSighted>());
        var hunters = world.Ships.Where(s => s.Team == Team.Pirates && !s.IsFort).Skip(before).ToList();
        Assert.Equal(relief.Ships, hunters.Count);
        Assert.All(hunters, h => Assert.True(((HunterBehavior)h.Behavior!).Relentless));

        // Two thirds down: the second; and no more after that.
        foreach (var fort in forts.Take(2 * forts.Count / 3 + 1))
            fort.Health = 0f;
        world.Step();
        Assert.Single(world.DrainEvents().OfType<ReliefFleetSighted>());
        foreach (var fort in forts)
            fort.Health = 0f;
        world.Step();
        Assert.Empty(world.DrainEvents().OfType<ReliefFleetSighted>());
        Assert.True(director.Cleared);
    }

    [Fact]
    public void ALowFortress_SendsForNoOne()
    {
        var (world, _) = At(Fortress(level: RunDirector.ReliefLevel - 1));
        foreach (var fort in world.Ships.Where(s => s.IsFort).ToList())
        {
            fort.Health = 0f;
            world.Step();
        }
        Assert.Empty(world.DrainEvents().OfType<ReliefFleetSighted>());
    }

    // ---- Bosses -------------------------------------------------------------------------------------------

    private static Ship CallBoss(World world, RunDirector director)
    {
        RunTicks(world, director.BossCountdownTicks + 1);
        return world.Ships.Single(s => s.IsBoss);
    }

    [Fact]
    public void ABoss_StopsAtTheEndOfEachPhase_ThenCallsEscorts_AndReloads()
    {
        var (world, director) = At(BossStop(round: 2));
        var boss = CallBoss(world, director);
        Assert.Equal(RunDirector.BossPhaseGates, boss.PhaseGates);
        var full = boss.Stats.MaxHealth;
        boss.Abilities[0]!.StartCooldown(0, 300, 1f);
        var escortsBefore = world.Ships.Count(s => s.Team == Team.Pirates && !s.IsBoss);

        // A blow far past the gate stops at it.
        world.DealDamage(boss, full, attackerShipId: world.GetPlayerShip(1)!.Id);
        Assert.Equal(full * RunDirector.BossPhaseGates[0], boss.Health, 1);
        Assert.Equal(1, boss.PhasesPassed);

        // And for a moment nothing hurts it.
        world.DealDamage(boss, 10f, attackerShipId: world.GetPlayerShip(1)!.Id);
        Assert.Equal(full * RunDirector.BossPhaseGates[0], boss.Health, 1);

        world.Step();
        Assert.Equal(1, Assert.Single(world.DrainEvents().OfType<BossPhaseChanged>()).Phase);
        Assert.Equal(escortsBefore + RunDirector.EscortsPerPhase(2), world.Ships.Count(s => s.Team == Team.Pirates && !s.IsBoss));
        Assert.True(boss.Abilities[0]!.IsReady);

        // Past the shift it can be hurt again, down to the next gate; past the last gate, sunk.
        RunTicks(world, World.PhaseShiftTicks);
        world.DealDamage(boss, full, attackerShipId: world.GetPlayerShip(1)!.Id);
        Assert.Equal(full * RunDirector.BossPhaseGates[1], boss.Health, 1);
        RunTicks(world, World.PhaseShiftTicks + 1);
        world.DealDamage(boss, full, attackerShipId: world.GetPlayerShip(1)!.Id);
        Assert.True(boss.IsSunk);
    }

    [Fact]
    public void ABoss_IsAsManyTimesSturdierAsThereAreSailors()
    {
        var alone = At(BossStop(1));
        var solo = CallBoss(alone.World, alone.Director);
        var (world, director) = At(BossStop(1), players: 4);
        var crew = CallBoss(world, director);
        Assert.Equal(solo.Stats.MaxHealth * 4f, crew.Stats.MaxHealth, 1);
        Assert.True(crew.Stats.WeaponDamage > solo.Stats.WeaponDamage);
    }

    // ---- Sinking ------------------------------------------------------------------------------------------

    [Fact]
    public void SinkingAgain_AtTheSameStop_KeepsYouOutLonger_AndANewStopForgetsIt()
    {
        Assert.Equal(new[] { 10, 20, 30, 40, 40 }, Enumerable.Range(1, 5).Select(d => Respawning.DelayTicksFor(d) / SimConstants.TickRate));

        var (world, _) = At(Fortress(level: 1), players: 2);
        world.GetPlayerShip(1)!.Health = 0f;
        world.Step();
        Assert.Equal(Respawning.DelayTicksFor(1), world.DrainEvents().OfType<PlayerSunk>().Single().RespawnTicks);
        RunTicks(world, Respawning.DelayTicksFor(1));
        world.GetPlayerShip(1)!.Health = 0f;
        world.Step();
        Assert.Equal(Respawning.DelayTicksFor(2), world.DrainEvents().OfType<PlayerSunk>().Single().RespawnTicks);
        Assert.Equal(2, world.Players[1].DeathsThisStop);
    }

    [Fact]
    public void ALoneSailor_ComesBackOnALifeboat_OnceAnAct_ThenTheRunEnds()
    {
        var (world, director) = At(Fortress(level: 1));
        var player = world.Players[1];
        Assert.Equal(Respawning.SoloLivesPerAct, player.ExtraLives);

        world.GetPlayerShip(1)!.Health = 0f;
        world.Step();
        Assert.False(world.IsRunOver);
        Assert.Equal(0, player.ExtraLives);
        RunTicks(world, Respawning.DelayTicksFor(1));
        var back = world.GetPlayerShip(1)!;
        Assert.True(Vector2.Distance(world.RegionEntry, back.Position) < 12f); // where the crew came in
        Assert.InRange(back.Health, back.Stats.MaxHealth * Respawning.ReturnHealth, back.Stats.MaxHealth * Respawning.ReturnHealth + 1f);

        back.Health = 0f;
        world.Step();
        Assert.True(world.IsRunOver);
        Assert.False(director.Cleared);
    }

    [Fact]
    public void Crews_GetNoLifeboats()
    {
        var (world, _) = At(Fortress(level: 1), players: 2);
        Assert.All(world.Players.Values, p => Assert.Equal(0, p.ExtraLives));
        foreach (var ship in world.Ships.Where(s => s.OwnerPlayerId is not null).ToList())
            ship.Health = 0f;
        world.Step();
        Assert.True(world.IsRunOver);
    }

    // ---- Friendly fire ------------------------------------------------------------------------------------

    [Fact]
    public void ACrewmatesBlast_HurtsAQuarterAsMuch_AsAnEnemysWould()
    {
        var world = new World(new Vector2(100, 100)) { Wind = Vector2.Zero, FriendlyFire = true };
        var gunner = world.SpawnShip(new Vector2(20, 50), 0f, ShipStats.Sloop, 1, Loadouts.Starting(WeaponCatalog.Mortar.Ability));
        var mate = world.SpawnShip(new Vector2(30, 50), 0f, ShipStats.Sloop, 2);
        var pirate = world.SpawnShip(new Vector2(30, 60), 0f, ShipStats.Sloop);
        var mateBefore = mate.Health;
        var pirateBefore = pirate.Health;

        world.LaunchStrike(gunner, mate.Position, radius: 2f, damage: 40f, flightTicks: 1);
        world.LaunchStrike(gunner, pirate.Position, radius: 2f, damage: 40f, flightTicks: 1);
        RunTicks(world, 3);

        Assert.Equal(40f * World.AllySplashScale, mateBefore - mate.Health, 0);
        Assert.Equal(40f, pirateBefore - pirate.Health, 0);
    }

    // ---- Ports --------------------------------------------------------------------------------------------

    [Fact]
    public void APort_SellsAHandOfCards_DoublingInPriceForEachOneBought()
    {
        Assert.Equal((150, 300, 600, 300), (Shipyards.CardPackCost(1, 0), Shipyards.CardPackCost(2, 0), Shipyards.CardPackCost(3, 0),
            Shipyards.CardPackCost(1, 1)));

        var port = new ChartNode(1, 2, 1, SeaChart.MiddleLane, NodeKind.Port, SeaChart.PortLevel(2, 1), Difficulty.Calm, Array.Empty<int>());
        var (world, _) = At(port);
        var ship = world.GetPlayerShip(1)!;
        world.AddGold(1, 1000);

        Assert.Equal(RejectionReason.NotAtShipyard, Shipyards.TryBuyCardPack(world, ship));

        var yard = world.Islands.Single(i => i.HasShipyard);
        var outward = Vector2.Normalize(world.RegionEntry - yard.Center);
        ship.Position = yard.ShoreToward(outward) + outward * 2f;
        ship.IsAnchored = true;
        Assert.Null(Shipyards.TryBuyCardPack(world, ship));
        var offer = Assert.Single(world.Players[1].CardOffers);
        Assert.Equal((OfferSource.Shop, yard.Level), (offer.Source, offer.Level));
        Assert.Equal(1000 - Shipyards.CardPackCost(2, 0), world.Players[1].Gold);
        Assert.Equal(Shipyards.CardPackCost(2, 1), Shipyards.CardPackCost(world, world.Players[1]));
    }

    [Fact]
    public void Upgrades_CostHalfAsMuchAgain_EachLevel()
    {
        var hull = UpgradeCatalog.Find("hull")!;
        Assert.Equal(new[] { 10, 15, 22, 34, 51, 76, 114, 171 }, Enumerable.Range(0, 8).Select(hull.CostAt));
    }
    // ---- Review fixes -------------------------------------------------------------------------------------

    [Fact]
    public void APiratesLongGun_KeepsItsOldWeight_WhileAPlayersIsHeavierAndQuicker()
    {
        var world = new World(new Vector2(100, 100));
        var player = world.SpawnShip(new Vector2(20, 20), 0f, ShipStats.Sloop, 1, Loadouts.Starting(WeaponCatalog.LongGun.Ability));
        var pirate = world.SpawnShip(new Vector2(60, 60), 0f, ShipStats.Sloop, abilities: Loadouts.Starting(WeaponCatalog.LongGun.Ability));
        var gun = WeaponCatalog.LongGun.Ability;

        Assert.Equal(30f, LongGun.DamageFor(player), 3);
        Assert.Equal(22f, LongGun.DamageFor(pirate), 3);
        Assert.Equal(4f * SimConstants.TickRate, gun.CooldownTicksFor(player), 3);
        Assert.Equal(5f * SimConstants.TickRate, gun.CooldownTicksFor(pirate), 3);
    }

    [Fact]
    public void RunningAground_CantCarryABossPastTheEndOfAPhase()
    {
        var (world, director) = At(BossStop(round: 1));
        var boss = CallBoss(world, director);
        var gate = boss.Stats.MaxHealth * RunDirector.BossPhaseGates[0];
        boss.Health = gate + 1f;

        // Drive it hard onto the nearest island.
        var island = world.Islands.OrderBy(i => i.DistanceTo(boss.Position)).First();
        var toShore = Vector2.Normalize(island.Center - boss.Position);
        boss.Behavior = null;
        boss.Position = island.ShoreToward(-toShore) - toShore * (boss.Stats.Length / 2f - 0.05f); // bow just short of the beach
        boss.Heading = MathF.Atan2(toShore.Y, toShore.X);
        boss.Speed = boss.Stats.MaxSpeed;
        boss.Throttle = ShipMovement.ThrottleLevels;
        boss.IsAground = false;
        world.Step();

        Assert.True(world.DrainEvents().Any(e => e is ShipGrounded grounded && grounded.ShipId == boss.Id),
            $"not aground: at {boss.Position}, {island.DistanceTo(boss.Position):0.00} from {island.Name}'s shore, speed {boss.Speed:0.0}, aground {boss.IsAground}");
        Assert.Equal(gate, boss.Health, 1);
        Assert.Equal(1, boss.PhasesPassed);
    }

    [Fact]
    public void TheSameSeed_DrawsTheSameSea_OnEveryLaunch()
    {
        // Pinned numbers: a process-salted hash (HashCode.Combine) would change these from one launch to the next.
        Assert.Equal(SeaChart.MixSeed(5, 1), SeaChart.MixSeed(5, 1));
        Assert.Equal(unchecked((5 * 486187739) ^ (1 * 16777619 + 0x5bd1e995)), SeaChart.MixSeed(5, 1));
        Assert.NotEqual(SeaChart.MixSeed(5, 1), SeaChart.MixSeed(5, 2));
    }

    [Fact]
    public void AHandBoughtAtAPort_DoesntPauseTheCrew_ButTheyWaitForItBeforeSailing()
    {
        var port = new ChartNode(1, 1, 1, SeaChart.MiddleLane, NodeKind.Port, SeaChart.PortLevel(1, 1), Difficulty.Calm, new[] { 2 });
        var next = new ChartNode(2, 1, 2, SeaChart.MiddleLane, NodeKind.Fortress, 2, Difficulty.Calm, Array.Empty<int>());
        var world = new World(Regions.StartSize) { Wind = Vector2.Zero };
        var director = new RunDirector(3) { Chart = new SeaChart(new[] { Start, port, next }) };
        world.Director = director;
        for (var id = 1; id <= 2; id++)
        {
            world.GetOrAddPlayer(id);
            world.SpawnShip(world.WorldSize / 2f, 0f, ShipStats.Sloop, id, Loadouts.Starting(WeaponCatalog.LongGun.Ability));
        }
        director.SailTo(world, port.Id);
        CardRewards.Offer(world, 1, CardRewards.Deal(new Random(1), CardCatalog.All, OfferSource.Shop, port.Level));

        Assert.False(world.IsPaused);
        var tick = world.Tick;
        world.Step();
        Assert.Equal(tick + 1, world.Tick);

        foreach (var id in world.Players.Keys)
            world.Enqueue(new ChooseCourseCommand(id, next.Id));
        RunTicks(world, 3);
        Assert.Equal(port.Id, director.NodeId); // still in port: player 1 hasn't chosen

        world.Enqueue(new ChooseCardCommand(1, world.Players[1].CardOffers[0].Cards[0].Id));
        RunTicks(world, 2);
        Assert.Equal(next.Id, director.NodeId);
    }

    [Fact]
    public void AReliefFleet_ComesFromTheFarSideOfTheSea_FacingTheCrew()
    {
        var (world, _) = At(Fortress(level: RunDirector.ReliefLevel, act: 2));
        foreach (var ship in world.Ships.Where(s => s.OwnerPlayerId is not null))
            ship.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 1_000_000f, "test"));
        var crew = world.GetPlayerShip(1)!.Position;
        var before = world.Ships.Where(s => s.Team == Team.Pirates).Select(s => s.Id).ToHashSet();
        foreach (var fort in world.Ships.Where(s => s.IsFort).ToList())
            fort.Health = 0f;
        world.Step();

        var fleet = world.Ships.Where(s => s.Team == Team.Pirates && !before.Contains(s.Id)).ToList();
        Assert.NotEmpty(fleet);
        var center = fleet.Aggregate(Vector2.Zero, (sum, s) => sum + s.Position) / fleet.Count;
        Assert.True(Vector2.Distance(center, crew) > world.WorldSize.X / 2f, $"relief mustered {Vector2.Distance(center, crew):0} from the crew");
        Assert.All(fleet, s =>
        {
            var toCrew = Vector2.Normalize(crew - s.Position);
            Assert.True(Vector2.Dot(s.Forward, toCrew) > 0.5f, "a relief ship isn't facing the crew");
        });
    }
}
