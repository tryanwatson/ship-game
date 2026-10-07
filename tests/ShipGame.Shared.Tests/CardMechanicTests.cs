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

    /// <summary>Under full sail at top speed, so it holds that speed through a step.</summary>
    private static void FullSail(Ship ship)
    {
        ship.Throttle = ShipMovement.ThrottleLevels;
        ship.Speed = ship.Stats.MaxSpeed;
    }

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    // ---- Perks ----------------------------------------------------------------------------------------------

    [Fact]
    public void SeaLegs_CompoundAndStopAtTheTightestTurn()
    {
        var (_, ship) = CreateWorld();
        Give(ship, "sea-legs", 6); // 60% tighter
        Assert.Equal(ShipStats.Sloop.MinTurnRadius * 0.4f, ship.Stats.MinTurnRadius, 4);

        // A second copy would compound to 16%, past the floor (two used to add up to -120%: a radius of zero).
        Give(ship, "sea-legs", 6);
        Assert.Equal(ShipStats.Sloop.MinTurnRadius * StatModifiers.TightestTurn, ship.Stats.MinTurnRadius, 4);
        Assert.Equal(ShipStats.Sloop.TurnRadiusAtMaxSpeed * StatModifiers.TightestTurn, ship.Stats.TurnRadiusAtMaxSpeed, 4);
    }

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
        Give(ship, "ram", 3); // 40 at a sloop's top speed
        FullSail(ship);
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

    [Theory]
    [InlineData(2f, 80f)] // twice a sloop's top speed, twice the damage
    [InlineData(0.5f, 20f)]
    public void Ram_HitsHarder_TheFasterTheRammer(float speedScale, float expected)
    {
        var (world, ship) = CreateWorld();
        Give(ship, "ram", 3); // 40 at a sloop's top speed
        ship.AddModifier(new StatModifier(StatId.MaxSpeed, ModifierKind.Percent, speedScale - 1f, "test"));
        FullSail(ship);
        var pirate = Pirate(world, ship.Position + new Vector2(1f, 0f));
        var full = pirate.Health;

        world.Step();

        Assert.Equal(full - expected, pirate.Health, 2);
    }

    [Fact]
    public void Ram_DoesNothing_AtAStandstill_AndIsStillReadyAfter()
    {
        var (world, ship) = CreateWorld();
        Give(ship, "ram", 3);
        var pirate = Pirate(world, ship.Position + new Vector2(1f, 0f));
        var full = pirate.Health;
        world.DrainEvents();

        world.Step();
        Assert.Equal(full, pirate.Health);
        Assert.Empty(world.DrainEvents().OfType<ShipRammed>());

        FullSail(ship);
        pirate.Position = ship.Position + new Vector2(1f, 0f);
        world.Step();
        Assert.Equal(full - 40f, pirate.Health, 2); // the nudge didn't spend the ram
    }

    [Fact]
    public void Ram_Card_ShowsWhatItDoesAtTheShipsSpeed()
    {
        var (_, ship) = CreateWorld();
        ship.AddModifier(new StatModifier(StatId.MaxSpeed, ModifierKind.Percent, 1f, "test")); // twice a sloop's top speed

        Assert.Equal("AT YOUR TOP SPEED: 80 DAMAGE.", new CardPick("ram", 3).DescriptionOn(ship));
        Assert.Null(new CardPick("heavy-shot", 3).DescriptionOn(ship));
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
        RunTicks(world, Statuses.Get(StatusId.Marked).Ticks + 1);
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

        RunTicks(world, Statuses.Get(StatusId.Slowed).Ticks + 1);
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

    // ---- Statuses ------------------------------------------------------------------------------------------

    [Fact]
    public void HeatedShot_SetsWhatItHitsBurning_AndTheBurnKeepsHurting()
    {
        var (world, ship) = CreateWorld(new LongGun());
        Give(ship, "heated-shot", 1); // 3 a second a stack
        var pirate = Pirate(world, ship.Position + new Vector2(6f, 0f));

        world.TryCastAbility(ship, AbilitySlot.One, pirate.Position);
        RunTicks(world, SimConstants.TickRate / 2);
        var burning = Assert.IsType<StatusEffect>(pirate.FindStatus(StatusId.Burning));
        Assert.Equal((1, 3f, ship.Id), (burning.Stacks, burning.Power, burning.SourceShipId));

        var afterHit = pirate.Health;
        RunTicks(world, SimConstants.TickRate * 2);
        Assert.Equal(afterHit - 6f, pirate.Health, 1);
        Assert.Equal(ship.Id, pirate.LastHitByShipId); // the burn is the shooter's, kill and all
    }

    [Fact]
    public void Burning_StacksToItsMost_ThenBurnsOut()
    {
        var (world, ship) = CreateWorld();
        var pirate = Pirate(world, new Vector2(150, 150));
        for (var i = 0; i < 30; i++)
            world.ApplyStatus(pirate, StatusId.Burning, 2f, ship.Id);
        Assert.Equal(Statuses.Get(StatusId.Burning).MaxStacks, pirate.StacksOf(StatusId.Burning));

        var full = pirate.Health;
        RunTicks(world, SimConstants.TickRate);
        Assert.Equal(full - 2f * 20, pirate.Health, 1);

        RunTicks(world, Statuses.Get(StatusId.Burning).Ticks);
        Assert.Empty(pirate.Statuses);
    }

    [Fact]
    public void Frenzy_StacksWithEveryKill_SpeedingTheReload_ThenWearsOff()
    {
        var (world, ship) = CreateWorld();
        Give(ship, "frenzy", 1); // +6% reload speed a stack
        var reload = ship.Stats.CooldownSpeed;
        var speed = ship.Stats.MaxSpeed;
        for (var i = 0; i < 2; i++)
        {
            var pirate = Pirate(world, new Vector2(150, 150 - 20 * i), health: 5f);
            world.DealDamage(pirate, 10f, ship.Id);
            world.Step();
        }

        Assert.Equal(2, ship.StacksOf(StatusId.Frenzy));
        Assert.Equal(reload * 1.12f, ship.Stats.CooldownSpeed, 3);
        Assert.Equal(speed * 1.06f, ship.Stats.MaxSpeed, 3);

        RunTicks(world, Statuses.Get(StatusId.Frenzy).Ticks + 1);
        Assert.Equal(0, ship.StacksOf(StatusId.Frenzy));
        Assert.Equal(reload, ship.Stats.CooldownSpeed, 3);
    }

    [Fact]
    public void PiratesAreRoused_WhenOneOfThemSinksNearby()
    {
        var (world, ship) = CreateWorld();
        var victim = Pirate(world, new Vector2(150, 150), health: 5f);
        var near = Pirate(world, new Vector2(155, 150));
        var far = Pirate(world, new Vector2(150, 190));

        world.DealDamage(victim, 10f, ship.Id);
        world.Step();

        Assert.Equal(1, near.StacksOf(StatusId.Frenzy));
        Assert.Equal(0, far.StacksOf(StatusId.Frenzy));
        Assert.Equal(0, ship.StacksOf(StatusId.Frenzy)); // no card, no frenzy for the player
    }

    [Fact]
    public void APiratesHeatedShot_SetsAPlayerBurning()
    {
        var (world, ship) = CreateWorld();
        var pirate = world.SpawnShip(ship.Position + new Vector2(6f, 0f), MathF.PI, ShipStats.PirateSloop,
            abilities: new Ability?[] { new LongGun(), null, null, null });
        pirate.AddCard(new CardPick("heated-shot", 1));

        world.TryCastAbility(pirate, AbilitySlot.One, ship.Position);
        RunTicks(world, SimConstants.TickRate);

        Assert.Equal(1, ship.StacksOf(StatusId.Burning));
    }

    [Fact]
    public void Statuses_DontComeBackWithARespawnedShip()
    {
        var (world, ship) = CreateWorld();
        world.ApplyStatus(ship, StatusId.Slowed, 0.5f, 99);
        Assert.Contains(ship.Modifiers, m => Statuses.IsStatusSource(m.Source));
        Assert.DoesNotContain(new[] { "slowed" }, s => ship.Modifiers.Any(m => m.Source == s)); // the old tag is gone

        world.RemoveStatus(ship, StatusId.Slowed);
        Assert.DoesNotContain(ship.Modifiers, m => Statuses.IsStatusSource(m.Source));
    }

    // ---- Cards that grow ------------------------------------------------------------------------------------

    [Fact]
    public void GunneryDrill_GrowsWithEveryFiftyBroadsideHits()
    {
        var (world, ship) = CreateWorld(new BroadsideVolley());
        var plain = BroadsideVolley.DamageFor(ship);
        Give(ship, "gunnery-drill", 1); // +2% a step
        Assert.Equal(plain, BroadsideVolley.DamageFor(ship), 3); // it starts at nothing

        ship.AddToTally(Tally.BroadsideHits, 49f);
        Assert.Equal(plain, BroadsideVolley.DamageFor(ship), 3);
        ship.AddToTally(Tally.BroadsideHits, 1f);
        Assert.Equal(plain * 1.02f, BroadsideVolley.DamageFor(ship), 3);
        ship.AddToTally(Tally.BroadsideHits, 70f);
        Assert.Equal(plain * 1.04f, BroadsideVolley.DamageFor(ship), 3);

        Assert.Equal("NOW +4% BROADSIDE DAMAGE. NEXT IN 30 BROADSIDE HITS.", new CardPick("gunnery-drill", 1).DescriptionOn(ship));
        Assert.Equal("EVERY 50 BROADSIDE HITS: +2% BROADSIDE DAMAGE.", new CardPick("gunnery-drill", 1).Description);
    }

    [Fact]
    public void AGrowingCard_SaysSo_BeforeItsGrown()
    {
        var (_, ship) = CreateWorld();
        Assert.Equal("NOW NOTHING YET. NEXT IN 5 KILLS.", new CardPick("bounty-hunter", 1).DescriptionOn(ship));
    }

    [Fact]
    public void ShipsTally_TheirHits_Kills_Damage_Gold_Sailing_AndTimeAtAnchor()
    {
        var (world, ship) = CreateWorld(new LongGun());
        var pirate = Pirate(world, ship.Position + new Vector2(6f, 0f), health: 10f);

        world.TryCastAbility(ship, AbilitySlot.One, pirate.Position);
        RunTicks(world, SimConstants.TickRate / 2);
        Assert.Equal(1f, ship.TallyOf(Tally.LongGunHits));
        Assert.Equal(1f, ship.TallyOf(Tally.Kills));
        Assert.Equal(10f, ship.TallyOf(Tally.DamageDealt), 2); // what it took, not the overkill
        Assert.Equal(0f, ship.TallyOf(Tally.MortarHits));

        world.DealDamage(ship, 7f, pirate.Id);
        Assert.Equal(7f, ship.TallyOf(Tally.DamageTaken), 2);

        var earned = ship.TallyOf(Tally.GoldEarned); // the kill paid some already
        Assert.True(earned > 0f);
        world.AddGold(PlayerId, 30);
        world.AddGold(PlayerId, -10); // spending earns nothing
        Assert.Equal(earned + 30f, ship.TallyOf(Tally.GoldEarned));

        ship.Throttle = ShipMovement.ThrottleLevels;
        ship.Speed = ship.Stats.MaxSpeed;
        var sailed = ship.TallyOf(Tally.TilesSailed);
        RunTicks(world, SimConstants.TickRate);
        Assert.Equal(sailed + ship.Stats.MaxSpeed, ship.TallyOf(Tally.TilesSailed), 1);

        ship.Speed = 0f;
        ship.Throttle = 0;
        ship.IsAnchored = true;
        RunTicks(world, SimConstants.TickRate * 2);
        Assert.Equal(2f, ship.TallyOf(Tally.SecondsAnchored), 1);
    }

    [Fact]
    public void AClientMirroringTallies_GrowsTheCardsToo()
    {
        var (_, ship) = CreateWorld();
        Give(ship, "sea-miles", 1); // +1% speed every 250 tiles
        var speed = ship.Stats.MaxSpeed;
        Assert.Equal(new[] { Tally.TilesSailed }, ship.GrowingTallies);

        ship.ReplaceTallies(new[] { (Tally.TilesSailed, 760f) });

        Assert.Equal(speed * 1.03f, ship.Stats.MaxSpeed, 3);
    }

    // ---- Anchoring ----------------------------------------------------------------------------------------

    [Fact]
    public void BatteryStation_WorksOnlyAtAnchor()
    {
        var (_, ship) = CreateWorld();
        var (reload, range) = (ship.Stats.CooldownSpeed, ship.Stats.WeaponRange);
        Give(ship, "battery-station", 1); // +30% reload, +15% range
        Assert.Equal((reload, range), (ship.Stats.CooldownSpeed, ship.Stats.WeaponRange));

        ship.Anchor = AnchorState.Down; // as a client mirroring the server sets it, too
        Assert.Equal(reload * 1.3f, ship.Stats.CooldownSpeed, 3);
        Assert.Equal(range * 1.15f, ship.Stats.WeaponRange, 3);

        ship.Anchor = AnchorState.Raising; // still held fast
        Assert.Equal(reload * 1.3f, ship.Stats.CooldownSpeed, 3);
        ship.Anchor = AnchorState.Weighed;
        Assert.Equal(reload, ship.Stats.CooldownSpeed, 3);
    }

    [Fact]
    public void DugIn_StacksEverySecondAtAnchor_AndFadesOnceItsComingUp()
    {
        var (world, ship) = CreateWorld();
        Give(ship, "dug-in", 1); // +4% damage a stack
        var damage = ship.Stats.WeaponDamage;
        ship.IsAnchored = true;

        RunTicks(world, SimConstants.TickRate * 3 + 5);
        Assert.Equal(3, ship.StacksOf(StatusId.Entrenched));
        Assert.Equal(damage * 1.12f, ship.Stats.WeaponDamage, 3);

        Anchoring.PressKey(ship); // start weighing: no more stacks
        RunTicks(world, SimConstants.TickRate * 2);
        Assert.Equal(3, ship.StacksOf(StatusId.Entrenched));
        RunTicks(world, SimConstants.TickRate * 2);
        Assert.Equal(0, ship.StacksOf(StatusId.Entrenched));
        Assert.Equal(damage, ship.Stats.WeaponDamage, 3);
    }

    [Fact]
    public void Braced_TurnsDamageAside_OnlyAtAnchor()
    {
        var (world, ship) = CreateWorld();
        Give(ship, "braced", 1); // 20% less
        var full = ship.Health;

        world.DealDamage(ship, 10f, 99);
        Assert.Equal(full - 10f, ship.Health, 2);

        ship.IsAnchored = true;
        world.DealDamage(ship, 10f, 99);
        Assert.Equal(full - 18f, ship.Health, 2);
    }

    [Fact]
    public void SpringLine_SwingsAnAnchoredShipRound()
    {
        static float Swung(bool spring)
        {
            var (world, ship) = CreateWorld();
            if (spring)
                Give(ship, "spring-line", 1); // 1.5x rowing
            ship.IsAnchored = true;
            ship.Rudder = 1;
            RunTicks(world, SimConstants.TickRate);
            return ship.Heading;
        }

        Assert.Equal(0f, Swung(spring: false));
        Assert.Equal(ShipMovement.RowingTurnRate * 1.5f, Swung(spring: true), 3);
    }

    [Fact]
    public void QuickAnchor_LetsGoAndWeighsFaster()
    {
        var (world, ship) = CreateWorld();
        Give(ship, "quick-anchor", 1); // twice as fast

        Anchoring.PressKey(ship);
        RunTicks(world, Anchoring.DropTicks / 2 + 1);
        Assert.Equal(AnchorState.Down, ship.Anchor);

        Anchoring.PressKey(ship);
        RunTicks(world, Anchoring.RaiseTicks / 2 + 1);
        Assert.Equal(AnchorState.Weighed, ship.Anchor);
    }

    [Fact]
    public void FloatingFortress_FiresEveryGunByItself_AtAnchor_LeadingAMovingTarget()
    {
        var (world, ship) = CreateWorld(new LongGun(), new Mortar());
        Give(ship, "floating-fortress", 3);
        var pirate = world.SpawnShip(ship.Position + new Vector2(10f, 0f), MathF.PI / 2f, ShipStats.PirateSloop); // heading +Y
        pirate.Speed = 3f;
        pirate.Throttle = ShipMovement.ThrottleLevels;

        world.Step();
        Assert.Empty(world.Projectiles); // under way: nothing
        Assert.Empty(world.Strikes);

        ship.IsAnchored = true;
        world.Step();
        var shot = Assert.Single(world.Projectiles);
        var shell = Assert.Single(world.Strikes);
        Assert.True(shot.Velocity.Y > 0f, "the long gun leads it");
        Assert.True(shell.Target.Y > pirate.Position.Y + 1f, "so does the mortar");
    }

    // ---- Broadside: the ring and its friends --------------------------------------------------------------------

    [Fact]
    public void ManOWar_FiresARingAllRound_ByItself_OnceAnEnemyIsInRange()
    {
        var (world, ship) = CreateWorld(new BroadsideVolley());
        Give(ship, "man-o-war", 3);

        world.Step();
        Assert.Empty(world.Projectiles); // nothing to shoot at

        Pirate(world, ship.Position + new Vector2(5f, 0f)); // dead ahead: no ordinary broadside bears
        world.Step();

        var ring = world.Projectiles;
        Assert.Equal(BroadsideVolley.CannonCount * BroadsideVolley.RingShotsPerCannon, ring.Count);
        Assert.Equal(ring.Count, BroadsideVolley.RingShotsFor(ship));
        Assert.Contains(ring, p => p.Velocity.X < -1f); // astern too
        Assert.Contains(ring, p => p.Velocity.Y < -1f);
        Assert.Contains(ring, p => p.Velocity.Y > 1f);
        var guns = ship.GetAbility(AbilitySlot.One)!;
        Assert.False(guns.IsChannelReady(BroadsideVolley.PortChannel));
        Assert.False(guns.IsChannelReady(BroadsideVolley.StarboardChannel)); // both decks fired
    }

    [Fact]
    public void ManOWar_NeverFiresItselfAtACrewmate()
    {
        var (world, ship) = CreateWorld(new BroadsideVolley());
        Give(ship, "man-o-war", 3);
        world.SpawnShip(ship.Position + new Vector2(4f, 0f), 0f, ShipStats.Sloop, PlayerId + 1);

        world.Step();

        Assert.Empty(world.Projectiles);
    }

    [Fact]
    public void ManOWar_Card_ShowsTheShipsRing()
    {
        var (_, ship) = CreateWorld(new BroadsideVolley());
        Assert.Equal("YOUR RING: 12 BALLS, EVERY 2.5 SECONDS.", new CardPick("man-o-war", 3).DescriptionOn(ship));
    }

    [Fact]
    public void GunCaptains_FireTheDeckThatBears_ByThemselves()
    {
        var (world, ship) = CreateWorld(new BroadsideVolley());
        Give(ship, "gun-captains", 1);
        Pirate(world, ship.Position + new Vector2(0f, 4f)); // abeam to starboard (Y-down)

        world.Step();

        Assert.Equal(BroadsideVolley.CannonCount, world.Projectiles.Count);
        Assert.All(world.Projectiles, p => Assert.True(p.Velocity.Y > 0f));
        var guns = ship.GetAbility(AbilitySlot.One)!;
        Assert.True(guns.IsChannelReady(BroadsideVolley.PortChannel));
        Assert.False(guns.IsChannelReady(BroadsideVolley.StarboardChannel));
    }

    [Fact]
    public void Grapeshot_FansMoreBallsFromEveryCannon_AtLessDamageEach()
    {
        var (world, ship) = CreateWorld(new BroadsideVolley());
        var plain = BroadsideVolley.DamageFor(ship);
        Give(ship, "grapeshot", 1); // 2 more each

        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + new Vector2(0f, 5f));

        Assert.Equal(BroadsideVolley.CannonCount * 3, world.Projectiles.Count);
        Assert.All(world.Projectiles, p => Assert.Equal(plain * BroadsideVolley.GrapeDamageFraction, p.Damage, 3));
        Assert.Equal(3, world.Projectiles.Select(p => MathF.Round(MathF.Atan2(p.Velocity.Y, p.Velocity.X), 3)).Distinct().Count());
    }

    [Fact]
    public void SkipShot_CarriesBallsOnPastTheirRange()
    {
        static bool Reaches(bool skipping)
        {
            var (world, ship) = CreateWorld(new BroadsideVolley());
            if (skipping)
                Give(ship, "skip-shot", 1);
            var pirate = Pirate(world, ship.Position + new Vector2(0f, 11f)); // beyond the broadside's 8
            world.TryCastAbility(ship, AbilitySlot.One, pirate.Position);
            RunTicks(world, SimConstants.TickRate * 2);
            return pirate.Health < pirate.Stats.MaxHealth;
        }

        Assert.False(Reaches(skipping: false));
        Assert.True(Reaches(skipping: true));
    }

    [Fact]
    public void HotGuns_HitsGiveTheReloadBack()
    {
        static int Remaining(bool hot)
        {
            var (world, ship) = CreateWorld(new BroadsideVolley());
            if (hot)
                Give(ship, "hot-guns", 7); // 10% a hit
            var pirate = Pirate(world, ship.Position + new Vector2(0f, 4f));
            world.TryCastAbility(ship, AbilitySlot.One, pirate.Position);
            RunTicks(world, SimConstants.TickRate / 2);
            return ship.GetAbility(AbilitySlot.One)!.RemainingTicks(BroadsideVolley.StarboardChannel);
        }

        var cold = Remaining(hot: false);
        Assert.True(Remaining(hot: true) <= cold - 20, "four hits should take 40% of a 75-tick reload off");
    }

    [Fact]
    public void Refund_NeverLetsAWeaponFireSoonerThanTheFloor()
    {
        var state = new AbilityState(new BroadsideVolley());
        state.StartCooldown(0, 75, 1f);
        for (var i = 0; i < 5; i++)
            state.TickCooldown();

        state.Refund(1f, minTicks: 15);

        Assert.Equal(10, state.RemainingTicks(0)); // fired 5 ticks ago; 10 more makes 15
        state.Refund(1f, minTicks: 15);
        Assert.Equal(10, state.RemainingTicks(0));
    }

    [Fact]
    public void IncendiaryShot_SetsTheWaterBurningWhereItHits()
    {
        var (world, ship) = CreateWorld(new BroadsideVolley());
        Give(ship, "incendiary-shot", 1);
        var pirate = Pirate(world, ship.Position + new Vector2(0f, 4f));

        world.TryCastAbility(ship, AbilitySlot.One, pirate.Position);
        RunTicks(world, SimConstants.TickRate / 2);

        Assert.NotEmpty(world.Fires);
        Assert.All(world.Fires, f => Assert.True(Vector2.Distance(f.Position, pirate.Position) < 2f));
    }

    // ---- Long gun -------------------------------------------------------------------------------------------

    [Fact]
    public void VolleyGun_FiresAFanOfShots()
    {
        var (world, ship) = CreateWorld(new LongGun());
        Give(ship, "volley-gun", 1); // 2 more

        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + new Vector2(10f, 0f));

        var angles = world.Projectiles.Select(p => MathF.Atan2(p.Velocity.Y, p.Velocity.X) * 180f / MathF.PI).OrderBy(a => a).ToList();
        Assert.Equal(3, angles.Count);
        Assert.Equal(-LongGun.VolleySpreadDegrees, angles[0], 2);
        Assert.Equal(0f, angles[1], 2);
        Assert.Equal(LongGun.VolleySpreadDegrees, angles[2], 2);
    }

    [Fact]
    public void Fork_SplitsAHitOnToTwoMoreEnemies()
    {
        var (world, ship) = CreateWorld(new LongGun());
        Give(ship, "fork", 3); // twice
        var first = Pirate(world, new Vector2(110, 100));
        var left = Pirate(world, new Vector2(114, 95));
        var right = Pirate(world, new Vector2(114, 105));

        world.TryCastAbility(ship, AbilitySlot.One, first.Position);
        RunTicks(world, SimConstants.TickRate * 2);

        Assert.All(new[] { first, left, right }, p => Assert.True(p.Health < p.Stats.MaxHealth));
    }

    [Fact]
    public void Headhunter_ReloadsTheLongGun_OnAKill()
    {
        static bool ReadyAfterKill(bool headhunter)
        {
            var (world, ship) = CreateWorld(new LongGun());
            if (headhunter)
                Give(ship, "headhunter", 3);
            var pirate = Pirate(world, ship.Position + new Vector2(6f, 0f), health: 5f);
            world.TryCastAbility(ship, AbilitySlot.One, pirate.Position);
            RunTicks(world, SimConstants.TickRate / 2);
            Assert.True(pirate.IsSunk || pirate.Health <= 0f);
            return ship.GetAbility(AbilitySlot.One)!.IsReady;
        }

        Assert.False(ReadyAfterKill(headhunter: false));
        Assert.True(ReadyAfterKill(headhunter: true));
    }

    [Fact]
    public void ExplosiveRounds_HurtShipsNearTheHit()
    {
        var (world, ship) = CreateWorld(new LongGun());
        Give(ship, "explosive-rounds", 1);
        var struck = Pirate(world, new Vector2(110, 100));
        var beside = Pirate(world, new Vector2(110, 102));

        world.TryCastAbility(ship, AbilitySlot.One, struck.Position);
        RunTicks(world, SimConstants.TickRate);

        Assert.True(beside.Health < beside.Stats.MaxHealth);
    }

    [Fact]
    public void BurningWake_LeavesFireAllAlongTheShot()
    {
        var (world, ship) = CreateWorld(new LongGun());
        Give(ship, "burning-wake", 1);

        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + new Vector2(16f, 0f));
        RunTicks(world, SimConstants.TickRate);

        // The whole 16-tile reach, a patch every 1.5 tiles.
        Assert.InRange(world.Fires.Count, 9, 11);
        Assert.All(world.Fires, f => Assert.Equal(100f, f.Position.Y, 2));
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
