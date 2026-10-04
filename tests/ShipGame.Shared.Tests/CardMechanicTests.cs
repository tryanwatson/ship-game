using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Tests;

/// <summary>What the cards with new rules actually do in play: perks, and the prismatic weapons.</summary>
public class CardMechanicTests
{
    private const int PlayerId = 1;

    private static (World world, Ship ship) CreateWorld(params Ability[] weapons)
    {
        var world = new World(new Vector2(200, 200)) { Wind = Vector2.Zero };
        var loadout = weapons.Cast<Ability?>().Concat(new Ability?[4]).Take(4).ToArray();
        var ship = world.SpawnShip(new Vector2(100, 100), 0f, ShipStats.Sloop, PlayerId, loadout);
        return (world, ship);
    }

    private static void Give(Ship ship, string card, int level) => ship.AddCard(new CardPick(card, level));

    private static Ship Pirate(World world, Vector2 at, float health = 1000f)
    {
        var pirate = world.SpawnShip(at, 0f, ShipStats.PirateSloop);
        pirate.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Flat, health - pirate.Stats.MaxHealth, "test"));
        pirate.AddModifier(new StatModifier(StatId.HealthRegen, ModifierKind.Multiplier, 0f, "test")); // so damage adds up exactly
        pirate.Health = pirate.Stats.MaxHealth;
        pirate.IsAnchored = true;
        return pirate;
    }

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    // ---- Perks ----------------------------------------------------------------------------------------------

    [Fact]
    public void Rowers_RowAsternFaster()
    {
        static float AsternSpeed(bool rowers)
        {
            var (world, ship) = CreateWorld();
            if (rowers)
                Give(ship, "rowers", 1); // twice as fast
            ship.Throttle = ShipMovement.AsternThrottle;
            RunTicks(world, SimConstants.TickRate * 6);
            return -ship.Speed;
        }

        Assert.Equal(AsternSpeed(rowers: false) * 2f, AsternSpeed(rowers: true), 2);
    }

    [Fact]
    public void Privateer_TakesMoreGold_FromAKill()
    {
        var (world, ship) = CreateWorld(new BroadsideVolley());
        Give(ship, "privateer", 6); // +75%
        var pirate = Pirate(world, new Vector2(150, 150), health: 50f);
        PirateLevels.Apply(pirate, 4); // 20 gold

        pirate.Health = 0f;
        pirate.LastHitByShipId = ship.Id;
        world.Step();

        Assert.Equal(35, world.Players[PlayerId].Gold);
    }

    [Fact]
    public void SecondWind_SavesTheShip_ThenNeedsTime()
    {
        var (world, ship) = CreateWorld();
        Give(ship, "second-wind", 3); // 30% health, every 90 seconds

        ship.Health = 0f;
        world.Step();
        Assert.Same(ship, world.GetPlayerShip(PlayerId));
        Assert.Equal(0.3f * ship.Stats.MaxHealth, ship.Health, 2);

        RunTicks(world, SimConstants.TickRate * 10);
        ship.Health = 0f;
        world.Step();
        Assert.Null(world.GetPlayerShip(PlayerId)); // not ready again yet
    }

    [Fact]
    public void PrizeCrew_HealsTheShip_ForEachKill()
    {
        var (world, ship) = CreateWorld();
        Give(ship, "prize-crew", 3); // 15%
        ship.Health = 50f;
        var pirate = Pirate(world, new Vector2(150, 150), health: 50f);

        pirate.Health = 0f;
        pirate.LastHitByShipId = ship.Id;
        world.Step();

        Assert.Equal(65f, ship.Health, 0);
    }

    [Fact]
    public void Ram_HurtsWhatItHits_OnceASecond()
    {
        var (world, ship) = CreateWorld();
        Give(ship, "ram", 3); // 40
        var pirate = Pirate(world, ship.Position + new Vector2(1f, 0f));
        var full = pirate.Health;
        world.DrainEvents();

        world.Step();
        Assert.Equal(full - 40f, pirate.Health, 2);
        var rammed = Assert.Single(world.DrainEvents().OfType<ShipRammed>());
        Assert.Equal((ship.Id, pirate.Id), (rammed.RammerShipId, rammed.TargetShipId));

        pirate.Position = ship.Position + new Vector2(1f, 0f);
        world.Step();
        Assert.Equal(full - 40f, pirate.Health, 2); // not again so soon
        Assert.Equal(ship.Stats.MaxHealth, ship.Health); // and the rammer takes nothing
    }

    [Fact]
    public void HuntersMark_MakesEveryonesHitsCountForMore()
    {
        var (world, ship) = CreateWorld();
        Give(ship, "hunters-mark", 3); // +25%
        var crewmate = world.SpawnShip(new Vector2(20, 20), 0f, ShipStats.Sloop, PlayerId + 1);
        var pirate = Pirate(world, new Vector2(150, 150));
        var full = pirate.Health;

        world.DealDamage(pirate, 10f, ship.Id);
        Assert.Equal(full - 10f, pirate.Health, 2); // the marking hit itself is plain
        world.DealDamage(pirate, 10f, crewmate.Id);
        Assert.Equal(full - 22.5f, pirate.Health, 2);

        world.Step();
        Assert.True(pirate.IsMarked);
        RunTicks(world, World.MarkTicks + 1);
        Assert.False(pirate.IsMarked);
        world.DealDamage(pirate, 10f, crewmate.Id);
        Assert.Equal(full - 32.5f, pirate.Health, 2);
    }

    [Fact]
    public void Echo_FiresEachWeaponAgain_AtAShareOfItsDamage()
    {
        var (world, ship) = CreateWorld(new LongGun());
        Give(ship, "echo", 3); // 40%
        world.DrainEvents();

        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + new Vector2(10, 0));
        var first = Assert.Single(world.DrainEvents().OfType<ProjectileSpawned>());
        RunTicks(world, World.EchoTicks + 1);

        var echo = Assert.Single(world.DrainEvents().OfType<ProjectileSpawned>());
        Assert.Equal(first.Damage * 0.4f, echo.Damage, 2);
        Assert.Equal(1f, ship.CastDamageScale);
    }

    // ---- Prismatic weapons ----------------------------------------------------------------------------------

    [Fact]
    public void TwinDecks_FiresBothSides_AndReloadsBoth()
    {
        var (world, ship) = CreateWorld(new BroadsideVolley());
        Give(ship, "twin-decks", 3);

        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + new Vector2(0, 5));

        Assert.Equal(2 * BroadsideVolley.CannonCount, world.Projectiles.Count);
        Assert.Contains(world.Projectiles, p => p.Velocity.Y > 0f);
        Assert.Contains(world.Projectiles, p => p.Velocity.Y < 0f);
        var broadside = ship.GetAbility(AbilitySlot.One)!;
        Assert.False(broadside.IsChannelReady(BroadsideVolley.PortChannel));
        Assert.False(broadside.IsChannelReady(BroadsideVolley.StarboardChannel));
    }

    [Fact]
    public void ChainShot_SlowsWhatItHits_ForAWhile()
    {
        var (world, ship) = CreateWorld(new BroadsideVolley());
        Give(ship, "chain-shot", 3); // 30%
        var pirate = Pirate(world, ship.Position + new Vector2(0, 4));
        var speed = pirate.Stats.MaxSpeed;

        world.TryCastAbility(ship, AbilitySlot.One, pirate.Position);
        RunTicks(world, SimConstants.TickRate / 2);
        Assert.Equal(speed * 0.7f, pirate.Stats.MaxSpeed, 3);

        RunTicks(world, World.SlowTicks + 1);
        Assert.Equal(speed, pirate.Stats.MaxSpeed, 3);
    }

    [Fact]
    public void Railgun_GoesThroughShips_AndOverLand()
    {
        var (world, ship) = CreateWorld(new LongGun());
        Give(ship, "railgun", 3);
        world.AddIsland(new Island(1, new[] { new Vector2(104, 98), new Vector2(106, 98), new Vector2(106, 102), new Vector2(104, 102) }));
        var near = Pirate(world, new Vector2(110, 100));
        var far = Pirate(world, new Vector2(122, 100));

        world.TryCastAbility(ship, AbilitySlot.One, far.Position);
        RunTicks(world, SimConstants.TickRate * 2);

        Assert.True(near.Health < near.Stats.MaxHealth, "the shot should fly over the rock and through the first ship");
        Assert.True(far.Health < far.Stats.MaxHealth);
        Assert.Equal(LongGun.Range * 2f, LongGun.RangeFor(ship), 3);
    }

    [Fact]
    public void Ricochet_BouncesOnToTheNextEnemy()
    {
        var (world, ship) = CreateWorld(new LongGun());
        Give(ship, "ricochet", 3); // once
        var first = Pirate(world, new Vector2(110, 100));
        var second = Pirate(world, new Vector2(112, 106));
        var third = Pirate(world, new Vector2(118, 110));

        world.TryCastAbility(ship, AbilitySlot.One, first.Position);
        RunTicks(world, SimConstants.TickRate * 2);

        Assert.True(first.Health < first.Stats.MaxHealth);
        Assert.True(second.Health < second.Stats.MaxHealth, "it should bounce to the nearest other enemy");
        Assert.Equal(third.Stats.MaxHealth, third.Health); // one bounce only
    }

    [Fact]
    public void CarpetBombing_WalksALineOfShells_OutToTheTarget()
    {
        var (world, ship) = CreateWorld(new Mortar());
        Give(ship, "carpet-bombing", 3); // 6 shells
        var target = ship.Position + new Vector2(15, 0);

        world.TryCastAbility(ship, AbilitySlot.One, target);

        var strikes = world.Strikes.OrderBy(s => s.ImpactTick).ToList();
        Assert.Equal(6, strikes.Count);
        Assert.All(strikes, s => Assert.Equal(ship.Position.Y, s.Target.Y, 2));
        Assert.Equal(target, strikes[^1].Target);
        for (var i = 1; i < strikes.Count; i++)
            Assert.True(strikes[i].Target.X > strikes[i - 1].Target.X, "each lands further out than the last");
        Assert.Equal(6, Enumerable.Range(0, 6).Select(i => Mortar.ShellLandingPoint(ship, target, i)).Distinct().Count()); // the aim preview agrees
    }

    [Fact]
    public void Firestorm_LeavesTheWaterBurning()
    {
        var (world, ship) = CreateWorld(new Mortar());
        Give(ship, "firestorm", 3); // 3 seconds, 10 a second
        var pirate = Pirate(world, ship.Position + new Vector2(12, 0));
        world.DrainEvents();

        world.TryCastAbility(ship, AbilitySlot.One, pirate.Position);
        var strike = world.Strikes.Single();
        RunTicks(world, (int)(strike.ImpactTick - world.Tick) + 1);
        var afterShell = pirate.Health;
        var fire = Assert.Single(world.DrainEvents().OfType<FireStarted>());
        Assert.Equal(pirate.Position, fire.Position);
        Assert.Single(world.Fires);

        RunTicks(world, SimConstants.TickRate);
        Assert.Equal(afterShell - 10f, pirate.Health, 0);
        RunTicks(world, SimConstants.TickRate * 3);
        Assert.Empty(world.Fires);
    }
}
