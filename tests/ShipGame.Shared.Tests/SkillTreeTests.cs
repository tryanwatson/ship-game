using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Tests;

public class SkillTreeTests
{
    private const int PlayerId = 1;

    // ---- Setup ------------------------------------------------------------------------------------------

    // An 8x8 shipyard island spanning x 40..48, y 26..34; the ship anchors 3 tiles off its west shore.
    private static (World world, Ship ship) Docked(WeaponOffer? start = null, int gold = 1000)
    {
        var world = new World(new Vector2(128, 128)) { Wind = Vector2.Zero };
        world.AddIsland(new Island(1, new[] { new Vector2(40, 26), new Vector2(48, 26), new Vector2(48, 34), new Vector2(40, 34) },
            hasShipyard: true));
        var ship = world.SpawnShip(new Vector2(37, 30), 0f, ShipStats.Sloop, PlayerId,
            Loadouts.Starting((start ?? WeaponCatalog.Broadside).Ability));
        world.Players[PlayerId].Gold = gold;
        ship.IsAnchored = true;
        world.Step();
        world.DrainEvents();
        return (world, ship);
    }

    /// <summary>Applies the command; returns why it was refused, or null if it went through.</summary>
    private static RejectionReason? Do(World world, Command command)
    {
        world.Enqueue(command);
        world.Step();
        return world.DrainEvents().OfType<CommandRejected>().FirstOrDefault()?.Reason;
    }

    private static RejectionReason? Buy(World world, string skillId) => Do(world, new PurchaseSkillCommand(PlayerId, skillId));

    private static int Gold(World world) => world.Players[PlayerId].Gold;

    /// <summary>Open water: a player shooter at (30, 30) facing +X with the given skills, and nothing else.</summary>
    private static (World world, Ship shooter) Range(params string[] skills)
    {
        var world = new World(new Vector2(128, 128)) { Wind = Vector2.Zero };
        var shooter = world.SpawnShip(new Vector2(30, 30), 0f, ShipStats.Sloop, PlayerId, Loadouts.FullArsenal);
        foreach (var skill in skills)
            shooter.AddSkill(SkillTrees.Find(skill)!);
        return (world, shooter);
    }

    /// <summary>A pirate lying side-on (facing +X) at <paramref name="position"/>, as a target.</summary>
    // No regeneration, so its health shows exactly what hit it.
    private static Ship Target(World world, Vector2 position) => world.SpawnShip(position, 0f, ShipStats.Sloop with { HealthRegen = 0f });

    private static void StepUntil(World world, Func<bool> done, int maxTicks = 300)
    {
        for (var t = 0; t < maxTicks && !done(); t++)
            world.Step();
    }

    private static float Damage(Ship ship) => ship.Stats.MaxHealth - ship.Health;

    // ---- The catalog ------------------------------------------------------------------------------------

    [Fact]
    public void EveryWeapon_HasASmallTree_WithARealChoice()
    {
        foreach (var weapon in WeaponCatalog.All)
        {
            var tree = SkillTrees.For(weapon.Id);
            Assert.InRange(tree.Count, 4, 6);
            Assert.Contains(tree, a => tree.Any(b => SkillTrees.AreExclusive(a, b))); // at least one either/or
            Assert.True(tree.Max(SkillTrees.TierOf) >= 3, $"{weapon.Id} should build up to a capstone");

            foreach (var skill in tree)
            {
                // Everything it refers to exists, in the same tree.
                foreach (var id in skill.Prerequisites.Concat(skill.Excludes))
                    Assert.Equal(weapon.Id, SkillTrees.Find(id)?.AbilityId);
                Assert.NotEmpty(skill.Effects);
                Assert.True(skill.Cost > 0);
            }
        }
        Assert.Equal(SkillTrees.All.Count, SkillTrees.All.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void StartingLoadout_HoldsOnlyTheChosenWeapon()
    {
        var loadout = Loadouts.Starting(WeaponCatalog.Mortar.Ability);

        Assert.IsType<Mortar>(loadout[0]);
        Assert.All(loadout.Skip(1), Assert.Null);
        Assert.Equal(Ship.AbilitySlotCount, loadout.Count);
    }

    // ---- Unlocking weapons ------------------------------------------------------------------------------

    [Fact]
    public void UnlockingAWeapon_CostsGold_AndTakesTheNextFreeSlot()
    {
        var (world, ship) = Docked(WeaponCatalog.Mortar, gold: 100);

        world.Enqueue(new UnlockAbilityCommand(PlayerId, LongGun.AbilityId));
        world.Step();

        Assert.IsType<LongGun>(ship.GetAbility(AbilitySlot.Two)!.Definition);
        Assert.Equal(100 - WeaponCatalog.LongGun.UnlockCost, Gold(world));
        Assert.Contains(world.DrainEvents(), e => e is AbilityUnlocked { AbilityId: LongGun.AbilityId, Slot: AbilitySlot.Two });

        Assert.Equal(RejectionReason.AlreadyOwned, Do(world, new UnlockAbilityCommand(PlayerId, LongGun.AbilityId)));
        Assert.Equal(RejectionReason.AlreadyOwned, Do(world, new UnlockAbilityCommand(PlayerId, Mortar.AbilityId)));
        Assert.Equal(RejectionReason.UnknownUpgrade, Do(world, new UnlockAbilityCommand(PlayerId, "cannonade")));
    }

    [Fact]
    public void UnlockingAWeapon_NeedsTheGold_AndAShipyard()
    {
        var (world, ship) = Docked(gold: WeaponCatalog.Mortar.UnlockCost - 1);
        Assert.Equal(RejectionReason.NotEnoughGold, Do(world, new UnlockAbilityCommand(PlayerId, Mortar.AbilityId)));

        world.Players[PlayerId].Gold = 1000;
        ship.IsAnchored = false;
        Assert.Equal(RejectionReason.NotAtShipyard, Do(world, new UnlockAbilityCommand(PlayerId, Mortar.AbilityId)));
        Assert.False(ship.HasAbility(Mortar.AbilityId));
        Assert.Equal(1000, Gold(world));
    }

    // ---- Buying skills ----------------------------------------------------------------------------------

    [Fact]
    public void Skills_CostGold_AndNeedTheirWeapon()
    {
        var (world, ship) = Docked(WeaponCatalog.Mortar, gold: 100);

        Assert.Equal(RejectionReason.AbilityLocked, Buy(world, "heavy-volley")); // broadside not unlocked yet
        Assert.Equal(SkillStatus.WeaponLocked, Shipyards.StatusOf(ship, SkillTrees.Find("heavy-volley")!));

        Assert.Null(Buy(world, "heavy-shell"));
        Assert.True(ship.HasSkill("heavy-shell"));
        Assert.Equal(100 - SkillTrees.Find("heavy-shell")!.Cost, Gold(world));
        Assert.Equal(RejectionReason.AlreadyOwned, Buy(world, "heavy-shell"));
    }

    [Fact]
    public void Skills_FollowTheirPrerequisites()
    {
        var (world, _) = Docked();

        Assert.Equal(RejectionReason.MissingPrerequisite, Buy(world, "point-blank"));
        Assert.Equal(RejectionReason.MissingPrerequisite, Buy(world, "thunderous-volley"));
        Assert.Null(Buy(world, "heavy-volley"));
        Assert.Equal(RejectionReason.MissingPrerequisite, Buy(world, "thunderous-volley")); // still needs a tier-2 skill
        Assert.Null(Buy(world, "point-blank"));
        Assert.Null(Buy(world, "thunderous-volley"));
    }

    [Fact]
    public void ChoosingABranch_LocksOutTheOther()
    {
        var (world, ship) = Docked();
        var heavy = SkillTrees.Find("heavy-volley")!;

        // Before buying, the shipyard can say what the choice would shut out.
        Assert.Equal(new[] { "rapid-guns", "improved-powder", "rolling-thunder" }, SkillTrees.WouldCloseOff(Array.Empty<string>(), heavy).Select(s => s.Id));

        Assert.Null(Buy(world, "heavy-volley"));

        Assert.Equal(SkillStatus.Excluded, Shipyards.StatusOf(ship, SkillTrees.Find("rapid-guns")!));
        Assert.Equal(SkillStatus.Excluded, Shipyards.StatusOf(ship, SkillTrees.Find("improved-powder")!));
        Assert.Equal(SkillStatus.Available, Shipyards.StatusOf(ship, SkillTrees.Find("point-blank")!));
        Assert.Equal(SkillStatus.NeedsPrerequisite, Shipyards.StatusOf(ship, SkillTrees.Find("thunderous-volley")!));
        Assert.Equal(RejectionReason.ExcludedByChoice, Buy(world, "rapid-guns"));
        Assert.Equal(RejectionReason.ExcludedByChoice, Buy(world, "improved-powder"));
    }

    [Theory]
    [InlineData(BroadsideVolley.AbilityId)]
    [InlineData(LongGun.AbilityId)]
    [InlineData(Mortar.AbilityId)]
    public void NoTree_CanBeBoughtOutEntirely(string abilityId)
    {
        var (world, ship) = Docked(WeaponCatalog.Find(abilityId), gold: 10_000);

        // Buy whatever's on offer until nothing is.
        while (SkillTrees.For(abilityId).FirstOrDefault(s => Shipyards.StatusOf(ship, s) == SkillStatus.Available) is { } next)
            Assert.Null(Buy(world, next.Id));

        Assert.Equal(3, ship.Skills.Count);
        Assert.True(ship.Skills.Count < SkillTrees.For(abilityId).Count);
    }

    [Fact]
    public void WeaponsAndSkills_SurviveSinking_AndComeBackOnTheNewShip()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var ship = world.SpawnShip(new Vector2(80, 96), 0f, ShipStats.Sloop, PlayerId, Loadouts.Starting(WeaponCatalog.LongGun.Ability));
        world.SpawnShip(new Vector2(100, 96), 0f, ShipStats.Sloop, 2, Loadouts.Starting(WeaponCatalog.Broadside.Ability)); // keeps the run going
        ship.SetAbility(AbilitySlot.Two, WeaponCatalog.Mortar.Ability);
        ship.AddSkill(SkillTrees.Find("rifled-barrel")!);

        ship.Health = 0f;
        world.Step();
        for (var t = 0; t <= Respawning.DelayTicks; t++)
            world.Step();

        var reborn = world.GetPlayerShip(PlayerId)!;
        Assert.NotSame(ship, reborn);
        Assert.IsType<LongGun>(reborn.GetAbility(AbilitySlot.One)!.Definition);
        Assert.IsType<Mortar>(reborn.GetAbility(AbilitySlot.Two)!.Definition);
        Assert.True(reborn.HasSkill("rifled-barrel"));
        Assert.Equal(LongGun.RangeFor(ship), LongGun.RangeFor(reborn));
    }

    // ---- Broadside --------------------------------------------------------------------------------------

    [Fact]
    public void HeavyVolley_FiresMoreCannon_ButReloadsSlower()
    {
        var (world, ship) = Range("heavy-volley");

        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + new Vector2(0, 5));

        Assert.Equal(BroadsideVolley.CannonCount + 2, world.Projectiles.Count);
        var broadside = ship.GetAbility(AbilitySlot.One)!;
        Assert.Equal(new BroadsideVolley().CooldownTicks * 1.2f, broadside.DurationTicks(BroadsideVolley.StarboardChannel), 0);
    }

    [Fact]
    public void RapidGuns_FiresFewerCannon_ButReloadsMuchFaster()
    {
        var (world, ship) = Range("rapid-guns");

        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + new Vector2(0, 5));

        Assert.Equal(BroadsideVolley.CannonCount - 1, world.Projectiles.Count);
        Assert.Equal(MathF.Round(new BroadsideVolley().CooldownTicks * 0.7f),
            ship.GetAbility(AbilitySlot.One)!.DurationTicks(BroadsideVolley.StarboardChannel), 0);
    }

    [Theory]
    [InlineData(3f, 1.5f)]  // within half the broadside's range: point blank
    [InlineData(6.5f, 1f)]  // further out: no bonus
    public void PointBlank_HitsHarder_UpClose(float distance, float expectedMultiplier)
    {
        var (plain, plainShooter) = Range("heavy-volley");
        var plainTarget = Target(plain, plainShooter.Position + new Vector2(0, distance));
        var (skilled, skilledShooter) = Range("heavy-volley", "point-blank");
        var skilledTarget = Target(skilled, skilledShooter.Position + new Vector2(0, distance));

        plain.TryCastAbility(plainShooter, AbilitySlot.One, plainTarget.Position);
        skilled.TryCastAbility(skilledShooter, AbilitySlot.One, skilledTarget.Position);
        StepUntil(plain, () => plain.Projectiles.Count == 0);
        StepUntil(skilled, () => skilled.Projectiles.Count == 0);

        Assert.True(Damage(plainTarget) > 0f);
        Assert.Equal(Damage(plainTarget) * expectedMultiplier, Damage(skilledTarget), 2);
    }

    [Fact]
    public void ImprovedPowder_HitsHarder_AtRestAndAtFullSail()
    {
        var (world, ship) = Range("rapid-guns", "improved-powder");

        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + new Vector2(0, 5));
        Assert.All(world.Projectiles, p => Assert.Equal(12.5f, p.Damage, 3));

        ship.Speed = ship.Stats.MaxSpeed;
        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + new Vector2(0, -5));
        Assert.All(world.Projectiles, p => Assert.Equal(12.5f, p.Damage, 3));
    }

    [Fact]
    public void ThunderousVolley_AddsBurst_WithoutLosingItsBranchIdentity()
    {
        var (world, ship) = Range("heavy-volley", "point-blank", "thunderous-volley");

        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + new Vector2(0, 5));

        Assert.Equal(BroadsideVolley.CannonCount + 4, world.Projectiles.Count);
        Assert.All(world.Projectiles, p => Assert.Equal(BroadsideVolley.Damage * 1.25f, p.Damage, 3));
        Assert.Equal(new BroadsideVolley().CooldownTicks * 1.4f,
            ship.GetAbility(AbilitySlot.One)!.DurationTicks(BroadsideVolley.StarboardChannel), 0);
    }

    // ---- Long gun ---------------------------------------------------------------------------------------

    [Fact]
    public void RifledBarrel_ShootsFasterAndFurther()
    {
        var (_, plain) = Range();
        var (world, ship) = Range("rifled-barrel");

        Assert.Equal(LongGun.Range * 1.25f, LongGun.RangeFor(ship), 3);
        world.TryCastAbility(ship, AbilitySlot.Two, ship.Position + new Vector2(0, 10));
        Assert.Equal(LongGun.SpeedFor(plain) * 1.4f, world.Projectiles[0].Velocity.Length(), 2);
    }

    [Fact]
    public void PiercingShot_PassesThroughTheFirstShip_IntoTheNext()
    {
        var (plain, plainShooter) = Range("heavy-shot");
        var plainNear = Target(plain, new Vector2(30, 34));
        var plainFar = Target(plain, new Vector2(30, 38));
        var (skilled, skilledShooter) = Range("heavy-shot", "piercing-shot");
        var skilledNear = Target(skilled, new Vector2(30, 34));
        var skilledFar = Target(skilled, new Vector2(30, 38));

        plain.TryCastAbility(plainShooter, AbilitySlot.Two, new Vector2(30, 45));
        skilled.TryCastAbility(skilledShooter, AbilitySlot.Two, new Vector2(30, 45));
        StepUntil(plain, () => plain.Projectiles.Count == 0);
        StepUntil(skilled, () => skilled.Projectiles.Count == 0);

        Assert.Equal(LongGun.Damage * 1.5f, Damage(plainNear), 2);
        Assert.Equal(0f, Damage(plainFar));
        Assert.Equal(LongGun.Damage * 1.5f, Damage(skilledNear), 2);
        Assert.Equal(LongGun.Damage * 1.5f, Damage(skilledFar), 2);
    }

    [Fact]
    public void Rangefinder_LongRangeHits_Refund35PercentOfFullReload()
    {
        var (world, ship) = Range("rifled-barrel", "rangefinder");
        Target(world, ship.Position + new Vector2(0, 18)); // beyond 60% of the rifled gun's reach
        var gun = ship.GetAbility(AbilitySlot.Two)!;

        world.TryCastAbility(ship, AbilitySlot.Two, ship.Position + new Vector2(0, 18));
        var duration = gun.DurationTicks(0);
        var ticks = 0;
        for (; world.Projectiles.Count > 0; ticks++)
            world.Step();

        var expected = duration - ticks - (int)MathF.Round(duration * 0.35f);
        Assert.InRange(gun.RemainingTicks(0), expected - 1, expected + 1);
    }

    [Fact]
    public void Rangefinder_ShortRangeHits_GiveNothingBack()
    {
        var (world, ship) = Range("rifled-barrel", "rangefinder");
        Target(world, ship.Position + new Vector2(0, 5));
        var gun = ship.GetAbility(AbilitySlot.Two)!;

        world.TryCastAbility(ship, AbilitySlot.Two, ship.Position + new Vector2(0, 5));
        var duration = gun.DurationTicks(0);
        var ticks = 0;
        for (; world.Projectiles.Count > 0; ticks++)
            world.Step();

        Assert.InRange(gun.RemainingTicks(0), duration - ticks - 1, duration - ticks + 1);
    }

    [Theory]
    [InlineData(18f, 1.75f)] // long range
    [InlineData(5f, 1f)]    // short range: no bonus
    public void Deadeye_HitsHarder_AtLongRange(float distance, float expectedMultiplier)
    {
        var (world, ship) = Range("rifled-barrel", "rangefinder", "deadeye");
        var target = Target(world, ship.Position + new Vector2(0, distance));

        world.TryCastAbility(ship, AbilitySlot.Two, target.Position);
        StepUntil(world, () => world.Projectiles.Count == 0);

        Assert.Equal(LongGun.Damage * expectedMultiplier, Damage(target), 2);
    }

    // ---- Mortar -----------------------------------------------------------------------------------------

    [Fact]
    public void HeavyShell_BurstsWider()
    {
        var (world, ship) = Range("heavy-shell");

        world.TryCastAbility(ship, AbilitySlot.Three, ship.Position + new Vector2(10, 0));

        Assert.Equal(Mortar.BlastRadius * 1.3f, world.Strikes.Single().Radius, 3);
    }

    [Fact]
    public void QuickFuse_LandsSooner()
    {
        var (_, plain) = Range();
        var (world, ship) = Range("quick-fuse");

        world.TryCastAbility(ship, AbilitySlot.Three, ship.Position + new Vector2(20, 0));

        var strike = world.Strikes.Single();
        Assert.True(strike.ImpactTick - strike.LaunchTick < Mortar.FlightTicks(plain, 20f) * 0.7f);
    }

    [Fact]
    public void ClusterShell_ScattersBlastsAroundTheImpact()
    {
        var (world, ship) = Range("heavy-shell", "cluster-shell");
        var aim = ship.Position + new Vector2(15, 0);

        world.TryCastAbility(ship, AbilitySlot.Three, aim);
        var shell = world.Strikes.Single();
        StepUntil(world, () => world.Strikes.All(s => s.Id != shell.Id));

        Assert.Equal(4, world.Strikes.Count);
        Assert.All(world.Strikes, bomblet =>
        {
            Assert.Equal(shell.Radius * Mortar.ClusterSpreadFraction, Vector2.Distance(aim, bomblet.Target), 3);
            Assert.Equal(shell.Damage * Mortar.ClusterDamageFraction, bomblet.Damage, 3);
            Assert.Null(bomblet.Cluster); // bomblets don't scatter bomblets
        });
        StepUntil(world, () => world.Strikes.Count == 0, Mortar.ClusterDelayTicks + 1);
        Assert.Empty(world.Strikes);
    }

    [Fact]
    public void Bombardment_FiresAWeakerSalvo_ThatLandsInSequence()
    {
        var (world, ship) = Range("quick-fuse", "bombardment");

        world.TryCastAbility(ship, AbilitySlot.Three, ship.Position + new Vector2(15, 0));

        var shells = world.Strikes.OrderBy(s => s.ImpactTick).ToList();
        Assert.Equal(3, shells.Count);
        Assert.All(shells, s => Assert.Equal(Mortar.Damage * 0.5f, s.Damage, 3));
        Assert.Equal(Mortar.SalvoGapTicks, shells[1].ImpactTick - shells[0].ImpactTick);
        Assert.Equal(Mortar.SalvoGapTicks, shells[2].ImpactTick - shells[1].ImpactTick);
        Assert.Equal(3, shells.Select(s => s.Target).Distinct().Count());
    }

    [Fact]
    public void Earthshaker_AmplifiesMainBlastAndBomblets_WithAModerateReload()
    {
        var (world, ship) = Range("heavy-shell", "cluster-shell", "earthshaker");
        var aim = ship.Position + new Vector2(80, 0);
        world.TryCastAbility(ship, AbilitySlot.Three, aim);

        var shell = world.Strikes.Single();
        Assert.Equal(Mortar.Range, Vector2.Distance(ship.Position, shell.Target), 2);
        Assert.Equal(3.75f, shell.Radius, 3);
        Assert.Equal(49f, shell.Damage, 3);
        Assert.Equal(207, ship.GetAbility(AbilitySlot.Three)!.DurationTicks(0));
        StepUntil(world, () => world.Strikes.All(s => s.Id != shell.Id));
        Assert.Equal(4, world.Strikes.Count);
        Assert.All(world.Strikes, s => Assert.Equal(12.25f, s.Damage, 3));
    }

    [Fact]
    public void RollingThunder_MaintainsRapidFire_AtAnySpeed()
    {
        var (world, ship) = Range("rapid-guns", "improved-powder", "rolling-thunder");
        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + new Vector2(0, 5));

        Assert.Equal(4, world.Projectiles.Count);
        Assert.All(world.Projectiles, p => Assert.Equal(12.5f, p.Damage, 3));
        Assert.Equal(38, ship.GetAbility(AbilitySlot.One)!.DurationTicks(BroadsideVolley.StarboardChannel));
    }

    [Fact]
    public void Hullbreaker_HitsThreeShips_WithFullDamage_ThenStops()
    {
        var (world, ship) = Range("heavy-shot", "piercing-shot", "hullbreaker");
        var targets = Enumerable.Range(1, 4).Select(i => Target(world, ship.Position + new Vector2(0, i * 3))).ToArray();
        world.TryCastAbility(ship, AbilitySlot.Two, ship.Position + new Vector2(0, 16));
        Assert.Equal(144, ship.GetAbility(AbilitySlot.Two)!.DurationTicks(0));
        StepUntil(world, () => world.Projectiles.Count == 0);

        Assert.All(targets.Take(3), target => Assert.Equal(55.5f, Damage(target), 2));
        Assert.Equal(0f, Damage(targets[3]));
    }

    [Fact]
    public void RainOfFire_FiresThreeStrongerShells_EveryFourAndAHalfSeconds()
    {
        var (world, ship) = Range("quick-fuse", "bombardment", "rain-of-fire");
        world.TryCastAbility(ship, AbilitySlot.Three, ship.Position + new Vector2(15, 0));

        Assert.Equal(3, world.Strikes.Count);
        Assert.All(world.Strikes, s => Assert.Equal(21f, s.Damage, 3));
        Assert.Equal(135, ship.GetAbility(AbilitySlot.Three)!.DurationTicks(0));
        foreach (var shell in world.Strikes.Skip(1))
            Assert.Equal(1.5f, Vector2.Distance(world.Strikes[0].Target, shell.Target), 3);
    }

    [Theory]
    [InlineData("heavy-volley", "thunderous-volley", "rolling-thunder")]
    [InlineData("rapid-guns", "rolling-thunder", "thunderous-volley")]
    [InlineData("rifled-barrel", "deadeye", "hullbreaker")]
    [InlineData("heavy-shot", "hullbreaker", "deadeye")]
    [InlineData("heavy-shell", "earthshaker", "rain-of-fire")]
    [InlineData("quick-fuse", "rain-of-fire", "earthshaker")]
    public void BranchChoices_LockOutTheOppositeCapstone(string root, string ownCapstone, string otherCapstone)
    {
        var skill = SkillTrees.Find(root)!;
        var (world, ship) = Docked(WeaponCatalog.Find(skill.AbilityId));
        Assert.Null(Buy(world, root));
        Assert.Equal(SkillStatus.NeedsPrerequisite, Shipyards.StatusOf(ship, SkillTrees.Find(ownCapstone)!));
        Assert.Equal(RejectionReason.ExcludedByChoice, Buy(world, otherCapstone));
        Assert.Contains(SkillTrees.WouldCloseOff(Array.Empty<string>(), skill), s => s.Id == otherCapstone);
    }
}
