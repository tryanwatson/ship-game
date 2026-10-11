using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Upgrades;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Tests;

/// <summary>Forts: the guns on a fortress's shore, and the garrison a fortress's level buys it.</summary>
public class FortTests
{
    private const int PlayerId = 1;

    /// <summary>A square fortress island, 10 tiles across, centered at (60, 60) on open water.</summary>
    private static (World world, Island island) CreateFortress(int level = 1)
    {
        var world = new World(new Vector2(200, 200)) { Wind = Vector2.Zero };
        var island = new Island(1, new[] { new Vector2(55, 55), new Vector2(65, 55), new Vector2(65, 65), new Vector2(55, 65) },
            level: level, isFortress: true);
        world.AddIsland(island);
        return (world, island);
    }

    private static Ship Player(World world, Vector2 position)
    {
        var ship = world.SpawnShip(position, 0f, ShipStats.Sloop, PlayerId, Loadouts.Starting(new LongGun()));
        ship.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 1_000_000f, "test"));
        ship.IsAnchored = true;
        return ship;
    }

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    [Fact]
    public void Forts_StandOnTheShore_AndNeverMove()
    {
        var (world, island) = CreateFortress();
        world.Wind = new Vector2(1f, 1f);
        var fort = Fortresses.SpawnFort(world, island, 0f, FortKind.Battery); // east shore
        Assert.Equal(new Vector2(65f - Fortresses.ShoreInset, 60f), fort.Position);
        var at = fort.Position;

        // A ship rammed into it gets pushed off; the fort doesn't budge.
        var rammer = world.SpawnShip(at + new Vector2(1.5f, 0f), MathF.PI, ShipStats.Sloop);
        rammer.Throttle = ShipMovement.ThrottleLevels;
        RunTicks(world, SimConstants.TickRate * 3);

        Assert.Equal(at, fort.Position);
        Assert.True(Vector2.Distance(rammer.Position, at) >= fort.Stats.Radius + rammer.Stats.Radius - 0.01f);
    }

    [Fact]
    public void ABattery_FiresOnShipsInReach_AndNotBeyond()
    {
        var (world, island) = CreateFortress();
        var fort = Fortresses.SpawnFort(world, island, 0f, FortKind.Battery);
        var reach = FortBehavior.Reach(fort);
        Assert.Equal(LongGun.Range * ShipStats.Fort.WeaponRange, reach, 3);

        var player = Player(world, fort.Position + new Vector2(reach + 5f, 0f));
        RunTicks(world, SimConstants.TickRate * 3);
        Assert.Equal(player.Stats.MaxHealth, player.Health);
        Assert.Equal(NpcStance.Patrolling, fort.Stance);

        player.Position = player.PreviousPosition = fort.Position + new Vector2(reach - 3f, 0f);
        RunTicks(world, SimConstants.TickRate * 3);
        Assert.True(player.Health < player.Stats.MaxHealth, "the battery should have hit");
        Assert.Equal(NpcStance.Hunting, fort.Stance);
    }

    [Fact]
    public void ABattery_FiresOverItsOwnIsland_ButNotThroughAnother()
    {
        var (world, island) = CreateFortress();
        var fort = Fortresses.SpawnFort(world, island, 0f, FortKind.Battery); // east shore
        var player = Player(world, new Vector2(48, 60)); // off the west shore: the island's between them
        RunTicks(world, SimConstants.TickRate * 3);
        Assert.True(player.Health < player.Stats.MaxHealth, "the battery should fire across its own island");

        var (world2, island2) = CreateFortress();
        var fort2 = Fortresses.SpawnFort(world2, island2, MathF.PI, FortKind.Battery); // west shore
        world2.AddIsland(new Island(2, new[] { new Vector2(46, 58), new Vector2(48, 58), new Vector2(48, 62), new Vector2(46, 62) }));
        var player2 = Player(world2, new Vector2(42, 60)); // behind a rock
        RunTicks(world2, SimConstants.TickRate * 3);
        Assert.Equal(player2.Stats.MaxHealth, player2.Health);
        Assert.Equal(0, fort2.GetAbility(AbilitySlot.One)!.CooldownRemainingTicks); // it held its fire
    }

    [Fact]
    public void ShotsFromTheSea_StrikeAFortOnTheShore()
    {
        var (world, island) = CreateFortress();
        var fort = Fortresses.SpawnFort(world, island, 0f, FortKind.Battery);
        fort.Behavior = null; // just a target
        var player = Player(world, fort.Position + new Vector2(10f, 0f));

        world.TryCastAbility(player, AbilitySlot.One, fort.Position);
        RunTicks(world, SimConstants.TickRate);

        Assert.True(fort.Health < fort.Stats.MaxHealth, "the shot should strike the fort, not just the beach");
        Assert.Empty(world.Projectiles);
    }

    [Fact]
    public void AShotFromAlongTheCoast_FliesOverTheBeachBesideAFort_ToItsWalls()
    {
        var (world, island) = CreateFortress();
        var fort = Fortresses.SpawnFort(world, island, 0f, FortKind.Battery); // east shore
        fort.Behavior = null;
        // Off the north-east corner: the line to the fort grazes the east shore a couple of tiles short of its walls.
        var player = Player(world, new Vector2(66.5f, 52f));
        Assert.True(world.LineHitsLand(player.Position, fort.Position, Projectile.DefaultRadius));

        world.TryCastAbility(player, AbilitySlot.One, fort.Position);
        RunTicks(world, SimConstants.TickRate);

        Assert.True(fort.Health < fort.Stats.MaxHealth, "the shore beside a fort shouldn't stop a shot aimed at it");
    }

    [Fact]
    public void ShotsStillStopOnAFortressShore_FarFromItsForts_AndOnceItsTaken()
    {
        var (world, island) = CreateFortress();
        var fort = Fortresses.SpawnFort(world, island, 0f, FortKind.Battery); // east shore
        fort.Behavior = null;
        var segmentNearFort = (from: new Vector2(66.5f, 52f), to: new Vector2(64.9f, 57.5f));
        var farFromFort = (from: new Vector2(60f, 50f), to: new Vector2(60f, 56f)); // into the north shore

        Assert.False(world.LineHitsLand(segmentNearFort.from, segmentNearFort.to, Projectile.DefaultRadius, clearForts: true));
        Assert.True(world.LineHitsLand(farFromFort.from, farFromFort.to, Projectile.DefaultRadius, clearForts: true));

        fort.Health = 0f;
        RunTicks(world, 1);
        Assert.True(world.LineHitsLand(segmentNearFort.from, segmentNearFort.to, Projectile.DefaultRadius, clearForts: true));
    }

    [Fact]
    public void AMortarTower_ShellsShipsFurtherOut_ThanABatteryReaches()
    {
        var (world, island) = CreateFortress();
        var tower = Fortresses.SpawnFort(world, island, 0f, FortKind.MortarTower);
        var battery = Fortresses.SpawnFort(world, island, MathF.PI, FortKind.Battery);
        Assert.True(FortBehavior.Reach(tower) > FortBehavior.Reach(battery));

        Player(world, tower.Position + new Vector2((FortBehavior.Reach(battery) + FortBehavior.Reach(tower)) / 2f, 0f));
        world.DrainEvents();
        world.Step();

        Assert.Equal(tower.Id, Assert.Single(world.DrainEvents().OfType<AreaStrikeLaunched>()).OwnerShipId);
    }

    [Fact]
    public void ABattery_LaysItsShotFirst_AndHoldsItsGunOnTheLine()
    {
        var (world, island) = CreateFortress();
        var fort = Fortresses.SpawnFort(world, island, 0f, FortKind.Battery); // east shore, facing east
        var player = Player(world, fort.Position + new Vector2(10f, 0f));

        world.Step();
        var warning = Assert.Single(world.Warnings);
        Assert.Equal(fort.Id, warning.ShipId);
        Assert.Empty(world.Projectiles);

        // The ship slips away north; the gun stays on the line it was laid on rather than following.
        player.Position = player.PreviousPosition = fort.Position + new Vector2(10f, -6f);
        for (var t = 0; t < LongGun.PirateWindupTicks - 1; t++)
            world.Step();
        var laidBearing = MathF.Atan2(warning.Target.Y - fort.Position.Y, warning.Target.X - fort.Position.X);
        Assert.InRange(MathF.Abs(Angles.Delta(fort.Heading, laidBearing)), 0f, 0.01f);

        RunTicks(world, SimConstants.TickRate);
        Assert.Equal(player.Stats.MaxHealth, player.Health); // out of the line before it fired: missed
    }

    [Fact]
    public void ABattery_HitsAShipThatStaysInTheLine()
    {
        var (world, island) = CreateFortress();
        var fort = Fortresses.SpawnFort(world, island, 0f, FortKind.Battery);
        var player = Player(world, fort.Position + new Vector2(10f, 0f));

        RunTicks(world, LongGun.PirateWindupTicks + SimConstants.TickRate);

        Assert.Equal(fort.Id, player.LastHitByShipId);
    }

    [Fact]
    public void ABatteryDestroyedWhileLaying_NeverFires()
    {
        var (world, island) = CreateFortress();
        var fort = Fortresses.SpawnFort(world, island, 0f, FortKind.Battery);
        var player = Player(world, fort.Position + new Vector2(10f, 0f));

        world.Step();
        Assert.Single(world.Warnings);
        fort.Health = 0f;
        RunTicks(world, LongGun.PirateWindupTicks + SimConstants.TickRate);

        Assert.Empty(world.Warnings);
        Assert.Equal(player.Stats.MaxHealth, player.Health);
    }

    [Fact]
    public void AMortarTowersShells_FlySlowAndBurstSmall_LikeAPiratesWould()
    {
        var (world, island) = CreateFortress();
        var tower = Fortresses.SpawnFort(world, island, 0f, FortKind.MortarTower);
        Player(world, tower.Position + new Vector2(15f, 0f));
        world.DrainEvents();
        world.Step();

        var strike = Assert.Single(world.Strikes);
        var distance = Vector2.Distance(tower.Position, strike.Target);
        var playerFlight = (Mortar.MinFlightSeconds + distance / (Mortar.ShellSpeed * tower.Stats.ProjectileSpeed)) * SimConstants.TickRate;
        Assert.True(strike.ImpactTick - strike.LaunchTick >= playerFlight * Mortar.PirateFlightTimeScale - 1);
        Assert.Equal(Mortar.BlastRadius * Mortar.PirateBlastRadiusScale, strike.Radius, 3);
    }

    [Theory]
    [InlineData(1, 2, 0, 1)]
    [InlineData(2, 2, 1, 2)]
    [InlineData(4, 3, 2, 3)]
    [InlineData(7, 5, 3, 4)]
    public void AFortressesLevel_SetsItsGarrison(int level, int batteries, int towers, int guards)
    {
        Assert.Equal((batteries, towers, guards), (Fortresses.Batteries(level), Fortresses.MortarTowers(level), Fortresses.GuardShips(level)));

        var (world, island) = CreateFortress(level);
        Fortresses.Garrison(world, island, players: 1, new Random(1));

        var forts = world.Ships.Where(s => s.IsFort).ToList();
        Assert.Equal(batteries, forts.Count(f => Fortresses.KindOf(f) == FortKind.Battery));
        Assert.Equal(towers, forts.Count(f => Fortresses.KindOf(f) == FortKind.MortarTower));
        Assert.All(forts, f => Assert.Equal((level, island.Id), (f.Level, f.FortIslandId!.Value)));
        var ships = world.Ships.Where(s => !s.IsFort).ToList();
        Assert.Equal(guards, ships.Count);
        Assert.All(ships, s => Assert.Equal(island.Id, Assert.IsType<GuardPost>(((HunterBehavior)s.Behavior!).Orders).IslandId));
        Assert.All(ships, s => Assert.Equal(level, s.Level));
    }

    [Fact]
    public void Forts_AreSturdierForBiggerCrews_AndPayAsMuchMore()
    {
        var (world, island) = CreateFortress();
        var solo = Fortresses.SpawnFort(world, island, 0f, FortKind.Battery, players: 1);
        var crew = Fortresses.SpawnFort(world, island, MathF.PI, FortKind.Battery, players: 3);

        // A bigger crew meets more forts, each sturdier: all told, the crew's share of everything to wear through.
        Assert.Equal(solo.Stats.MaxHealth * Fortresses.FortHealthScale(3), crew.Stats.MaxHealth, 3);
        Assert.Equal(Fortresses.CrewScale(3), Fortresses.FortHealthScale(3) * Fortresses.CountScale(3), 3);
        Assert.Equal(crew.Stats.MaxHealth, crew.Health);
        // Gold follows the health: three sailors share it, and the forts together pay CrewScale times a lone sailor's.
        Assert.Equal(KillRewards.GoldFor(solo) * Fortresses.FortHealthScale(3), KillRewards.GoldFor(crew), 0);
        // And its guns hit a little harder, since not all of them can bear on every sailor.
        Assert.Equal(solo.Stats.WeaponDamage + 2 * Fortresses.DamagePerExtraPlayer, crew.Stats.WeaponDamage, 3);
    }

    [Fact]
    public void ATakenFortress_IsNoPort_AndIsPlunderedWithoutAsking()
    {
        var (world, island) = CreateFortress();
        var ship = world.SpawnShip(new Vector2(67.5f, 60f), 0f, ShipStats.Sloop, PlayerId);
        ship.IsAnchored = true;
        world.AddGold(PlayerId, 500);

        world.TakeFortress(island);
        world.Step();

        Assert.False(world.IsPort(island)); // ports are stops of their own on the chart
        Assert.Null(Shipyards.DockedAt(world, ship));
        for (var i = 0; i < Plundering.DurationTicks + 5; i++)
            world.Step();
        Assert.True(world.IsPlundered(island));
    }

    [Fact]
    public void AFortress_CantBePlundered_UntilItsTaken()
    {
        var (world, island) = CreateFortress();
        var anchorage = new Vector2(67.5f, 60f);
        Assert.Null(Plundering.PlunderableFrom(world, anchorage));

        world.TakeFortress(island);

        Assert.Same(island, Plundering.PlunderableFrom(world, anchorage));
        Assert.False(world.TakeFortress(island)); // only once
    }
}
