using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Tests;

/// <summary>The long gun (3, skillshot) and the mortar (4, area strike): both aimed at the cursor.</summary>
public class AimedWeaponTests
{
    private const int PlayerId = 1;

    private static (World world, Ship ship) CreateWorld()
    {
        var world = new World(new Vector2(128, 128)) { Wind = Vector2.Zero };
        var ship = world.SpawnShip(new Vector2(60, 60), 0f, ShipStats.Sloop, PlayerId, Loadouts.Sloop);
        world.DrainEvents();
        return (world, ship);
    }

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    private static void Cast(World world, AbilitySlot slot, Vector2 aim)
    {
        world.Enqueue(new CastAbilityCommand(PlayerId, slot, aim));
        world.Step();
    }

    [Fact]
    public void PlayerSloop_CarriesBothNewWeapons_PiratesDoNot()
    {
        Assert.IsType<LongGun>(Loadouts.Sloop[(int)AbilitySlot.Two]);
        Assert.IsType<Mortar>(Loadouts.Sloop[(int)AbilitySlot.Three]);
        Assert.IsType<BroadsideVolley>(Loadouts.Sloop[(int)AbilitySlot.One]);
        Assert.Null(Loadouts.Sloop[(int)AbilitySlot.Four]);
        Assert.IsType<BroadsideVolley>(Loadouts.Pirate[(int)AbilitySlot.One]); // pirates: broadside only
        Assert.All(Loadouts.Pirate.Skip(1), Assert.Null);
        Assert.NotNull(AbilityRegistry.Find("long-gun"));
        Assert.NotNull(AbilityRegistry.Find("mortar"));
    }

    [Theory]
    [InlineData(10f, 0f)]   // ahead
    [InlineData(-10f, 0f)]  // astern: unlike broadsides, it fires any way
    [InlineData(3f, -7f)]   // off the port bow
    public void LongGun_FiresOneShotTowardTheAimPoint(float dx, float dy)
    {
        var (world, ship) = CreateWorld();

        Cast(world, AbilitySlot.Two, ship.Position + new Vector2(dx, dy));

        var shot = Assert.Single(world.Projectiles);
        var expected = Vector2.Normalize(new Vector2(dx, dy));
        Assert.True(Vector2.Dot(Vector2.Normalize(shot.Velocity), expected) > 0.999f);
        Assert.Equal(LongGun.ProjectileSpeed, shot.Velocity.Length(), 3);
        Assert.Equal(LongGun.ShotRadius, shot.Radius);
    }

    [Fact]
    public void LongGun_HitsTheFirstShipInItsPath_Only()
    {
        var (world, ship) = CreateWorld();
        var near = world.SpawnShip(ship.Position + new Vector2(6, 0), MathF.PI / 2f, ShipStats.Sloop);
        var far = world.SpawnShip(ship.Position + new Vector2(11, 0), MathF.PI / 2f, ShipStats.Sloop);

        Cast(world, AbilitySlot.Two, far.Position);
        RunTicks(world, SimConstants.TickRate);

        Assert.Equal(near.Stats.MaxHealth - LongGun.Damage, near.Health, 3);
        Assert.Equal(far.Stats.MaxHealth, far.Health);
    }

    [Fact]
    public void LongGun_RangeScalesWithTheRangeUpgrade()
    {
        var (world, ship) = CreateWorld();
        ship.AddModifier(new StatModifier(StatId.WeaponRange, ModifierKind.Percent, 0.25f, "test"));

        Cast(world, AbilitySlot.Two, ship.Position + new Vector2(5, 0));
        var shot = world.Projectiles[0];
        var reach = (shot.RemainingTicks + 1) * SimConstants.TickDelta * shot.Velocity.Length();

        Assert.InRange(reach, LongGun.Range * 1.25f, LongGun.Range * 1.25f + 1f);
    }

    [Fact]
    public void Mortar_LandsAfterAFlightTime_AndHurtsEveryHostileShipInTheBlast()
    {
        var (world, ship) = CreateWorld();
        var aim = ship.Position + new Vector2(15, 0);
        var a = world.SpawnShip(aim + new Vector2(1, 0), 0f, ShipStats.Sloop);
        var b = world.SpawnShip(aim + new Vector2(-1, 1), MathF.PI / 2f, ShipStats.Sloop);
        var clear = world.SpawnShip(aim + new Vector2(0, 6), 0f, ShipStats.Sloop);
        foreach (var s in new[] { a, b, clear })
            s.IsAnchored = true;

        Cast(world, AbilitySlot.Three, aim);
        var strike = Assert.Single(world.Strikes);
        Assert.Equal(aim, strike.Target);
        Assert.Equal(Mortar.FlightTicks(ship, 15f), strike.ImpactTick - strike.LaunchTick);

        // The shell bursts during the step whose tick counter equals ImpactTick.
        RunTicks(world, (int)(strike.ImpactTick - world.Tick));
        Assert.Equal(a.Stats.MaxHealth, a.Health); // still in the air

        world.Step();
        Assert.Empty(world.Strikes);
        Assert.Equal(a.Stats.MaxHealth - Mortar.Damage, a.Health, 3);
        Assert.Equal(b.Stats.MaxHealth - Mortar.Damage, b.Health, 3);
        Assert.Equal(clear.Stats.MaxHealth, clear.Health);
        Assert.Contains(world.DrainEvents(), e => e is AreaStrikeImpact impact && impact.StrikeId == strike.Id);
    }

    [Fact]
    public void Mortar_AimBeyondRange_IsPulledInToMaxRange()
    {
        var (world, ship) = CreateWorld();

        Cast(world, AbilitySlot.Three, ship.Position + new Vector2(0, 100));

        var strike = Assert.Single(world.Strikes);
        Assert.Equal(Mortar.Range, Vector2.Distance(ship.Position, strike.Target), 3);
    }

    [Fact]
    public void Mortar_SparesItsOwnShipAndFriends_UnlessFriendlyFire()
    {
        foreach (var friendlyFire in new[] { false, true })
        {
            var (world, ship) = CreateWorld();
            world.FriendlyFire = friendlyFire;
            var ally = world.SpawnShip(ship.Position + new Vector2(1.5f, 0), 0f, ShipStats.Sloop, 2);
            ally.IsAnchored = true;
            ship.IsAnchored = true;

            Cast(world, AbilitySlot.Three, ship.Position); // right on top of ourselves
            RunTicks(world, SimConstants.TickRate * 2);

            Assert.Equal(ship.Stats.MaxHealth, ship.Health);
            Assert.Equal(friendlyFire, ally.Health < ally.Stats.MaxHealth);
        }
    }

    [Fact]
    public void Mortar_FliesOverIslands()
    {
        var (world, ship) = CreateWorld();
        world.AddIsland(new Island(1, new[] { new Vector2(64, 55), new Vector2(68, 55), new Vector2(68, 65), new Vector2(64, 65) }));
        var target = world.SpawnShip(new Vector2(74, 60), MathF.PI / 2f, ShipStats.Sloop);
        target.IsAnchored = true;

        Cast(world, AbilitySlot.Three, target.Position);
        RunTicks(world, SimConstants.TickRate * 2);

        Assert.True(target.Health < target.Stats.MaxHealth);
    }

    [Fact]
    public void Mortar_KillsPayTheShooter()
    {
        var (world, ship) = CreateWorld();
        var target = world.SpawnShip(ship.Position + new Vector2(10, 0), 0f, ShipStats.Sloop);
        target.IsAnchored = true;
        target.Health = 1f;

        Cast(world, AbilitySlot.Three, target.Position);
        RunTicks(world, SimConstants.TickRate * 2);

        Assert.DoesNotContain(target, world.Ships);
        Assert.Equal(KillRewards.Gold, world.Players[PlayerId].Gold);
    }

    [Fact]
    public void BothWeapons_HaveCooldowns()
    {
        var (world, ship) = CreateWorld();

        Cast(world, AbilitySlot.Two, ship.Position + Vector2.UnitX);
        Cast(world, AbilitySlot.Three, ship.Position + Vector2.UnitX * 10);
        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.Two, Vector2.Zero));
        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.Three, Vector2.Zero));
        world.Step();

        Assert.Equal(2, world.DrainEvents().OfType<CommandRejected>().Count(r => r.Reason == RejectionReason.OnCooldown));
    }
}
