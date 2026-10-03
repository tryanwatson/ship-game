using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Tests;

public class ShipyardTests
{
    private const int PlayerId = 1;

    // An 8x8 island spanning x 40..48, y 26..34; the ship anchors 3 tiles off its west shore.
    private static Island Isle(bool shipyard) =>
        new(1, new[] { new Vector2(40, 26), new Vector2(48, 26), new Vector2(48, 34), new Vector2(40, 34) }, hasShipyard: shipyard);

    private static (World world, Ship ship) Docked(bool shipyard = true, int gold = 1000, bool anchored = true)
    {
        var world = new World(new Vector2(128, 128)) { Wind = Vector2.Zero };
        world.AddIsland(Isle(shipyard));
        var ship = world.SpawnShip(new Vector2(37, 30), 0f, ShipStats.Sloop, PlayerId, Loadouts.FullArsenal);
        world.Players[PlayerId].Gold = gold;
        if (anchored)
        {
            ship.IsAnchored = true;
            world.Step();
        }
        return (world, ship);
    }

    private static void Buy(World world, string id)
    {
        world.Enqueue(new PurchaseUpgradeCommand(PlayerId, id));
        world.Step();
    }

    private static int Gold(World world) => world.Players[PlayerId].Gold;

    [Fact]
    public void Map_HasSomeShipyards()
    {
        var islands = Archipelago.CreateIslands();
        Assert.InRange(islands.Count(i => i.HasShipyard), 2, islands.Count - 1);
    }

    [Fact]
    public void Shipyard_DoesNotPlunderUntilAsked()
    {
        var (world, ship) = Docked();

        for (var t = 0; t < Plundering.DurationTicks * 2; t++)
            world.Step();
        Assert.Equal(1000, Gold(world));

        world.Enqueue(new ChoosePlunderCommand(PlayerId));
        for (var t = 0; t < Plundering.DurationTicks + 1; t++)
            world.Step();
        Assert.Equal(1000 + Island.DefaultPlunderGold, Gold(world));

        // Consent is per plunder: when the cooldown ends, it doesn't start again on its own.
        for (var t = 0; t < Plundering.CooldownTicks + Plundering.DurationTicks * 2; t++)
            world.Step();
        Assert.Equal(1000 + Island.DefaultPlunderGold, Gold(world));
        Assert.Equal(0f, Plundering.Progress(ship));
    }

    [Fact]
    public void ChoosingPlunder_IsIgnoredWhileTheIslandIsOnCooldown()
    {
        var (world, ship) = Docked();
        world.StartPlunderCooldown(world.Islands[0], 100);

        world.Enqueue(new ChoosePlunderCommand(PlayerId));
        world.Step();

        Assert.Null(ship.PlunderConsentIslandId);
    }

    [Fact]
    public void Purchase_SpendsGold_RaisesLevel_AndCostsRise()
    {
        var (world, ship) = Docked(gold: 100);
        var speed = UpgradeCatalog.Find("speed")!;

        Buy(world, "speed");
        Buy(world, "speed");

        Assert.Equal(2, Shipyards.Level(ship, speed));
        Assert.Equal(100 - speed.CostAt(0) - speed.CostAt(1), Gold(world));
        Assert.True(speed.CostAt(1) > speed.CostAt(0));
        Assert.Equal(ShipStats.Sloop.MaxSpeed * (1f + 2 * speed.ValuePerLevel), ship.Stats.MaxSpeed, 4);
    }

    [Fact]
    public void Purchase_RequiresGold()
    {
        var (world, ship) = Docked(gold: 5);

        Assert.Equal(PurchaseResult.NotEnoughGold, Shipyards.TryPurchase(world, ship, "speed"));
        Assert.Equal(5, Gold(world));
        Assert.Empty(ship.Modifiers);
    }

    [Fact]
    public void Purchase_RequiresBeingAnchoredAtAShipyard()
    {
        var (atPlainIsland, ship1) = Docked(shipyard: false);
        Assert.Equal(PurchaseResult.NotAtShipyard, Shipyards.TryPurchase(atPlainIsland, ship1, "speed"));

        var (underWay, ship2) = Docked(anchored: false);
        Assert.Equal(PurchaseResult.NotAtShipyard, Shipyards.TryPurchase(underWay, ship2, "speed"));
    }

    [Fact]
    public void Purchase_StopsAtMaxLevel()
    {
        var (world, ship) = Docked();
        var reload = UpgradeCatalog.Find("reload")!;

        for (var i = 0; i < reload.MaxLevel; i++)
            Assert.Equal(PurchaseResult.Purchased, Shipyards.TryPurchase(world, ship, "reload"));

        Assert.Equal(PurchaseResult.MaxLevel, Shipyards.TryPurchase(world, ship, "reload"));
        Assert.Equal(reload.MaxLevel, Shipyards.Level(ship, reload));
    }

    [Fact]
    public void Catalog_HasTheRequestedUpgrades()
    {
        var ids = UpgradeCatalog.All.Select(u => u.Id).ToList();
        Assert.Equal(new[] { "hull", "speed", "reload", "damage", "shot-speed", "range", "agility" }, ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void DamageUpgrade_HitsHarder()
    {
        var (world, ship) = Docked();
        Buy(world, "damage");
        Buy(world, "damage");

        world.TryCastAbility(ship, AbilitySlot.One, Vector2.Zero);
        Assert.All(world.Projectiles, p => Assert.Equal(BroadsideVolley.Damage * 1.2f, p.Damage, 4));
    }

    [Fact]
    public void RangeAndShotSpeedUpgrades_ChangeHowFarAndFastShotsFly()
    {
        var (world, ship) = Docked();
        Buy(world, "range");
        Buy(world, "shot-speed");

        world.TryCastAbility(ship, AbilitySlot.One, Vector2.Zero);
        var shot = world.Projectiles[0];

        Assert.Equal(BroadsideVolley.ProjectileSpeed * 1.1f, MathF.Abs(shot.Velocity.Y), 3);
        var reach = shot.RemainingTicks * SimConstants.TickDelta * MathF.Abs(shot.Velocity.Y);
        Assert.InRange(reach, BroadsideVolley.Range * 1.1f, BroadsideVolley.Range * 1.1f + 0.6f);
        Assert.Equal(BroadsideVolley.Range * 1.1f, BroadsideVolley.RangeFor(ship), 4);
    }

    [Fact]
    public void HullUpgrade_AddsFlatMaxHp_AndTheSameToCurrentHealth()
    {
        var (world, ship) = Docked();
        ship.Health = 60f;

        Buy(world, "hull");
        Buy(world, "hull");

        Assert.Equal(ShipStats.Sloop.MaxHealth + 40f, ship.Stats.MaxHealth, 4);
        Assert.Equal(100f, ship.Health, 4);
    }

    [Fact]
    public void AgilityUpgrade_TightensTurns()
    {
        var (world, ship) = Docked();
        Buy(world, "agility");

        Assert.Equal(ShipStats.Sloop.MinTurnRadius * 0.92f, ship.Stats.MinTurnRadius, 4);
        Assert.Equal(ShipStats.Sloop.TurnRadiusAtMaxSpeed * 0.92f, ship.Stats.TurnRadiusAtMaxSpeed, 4);
    }
}
