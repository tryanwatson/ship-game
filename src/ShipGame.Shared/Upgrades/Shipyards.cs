using System.Numerics;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Upgrades;

public enum PurchaseResult
{
    Purchased,
    NotAtShipyard,
    UnknownUpgrade,
    MaxLevel,
    NotEnoughGold,
}

/// <summary>Shipyard rules: who can shop, what it costs, and the purchase itself.</summary>
public static class Shipyards
{
    /// <summary>The shipyard island a ship riding at anchor here can trade with, if any (same range as plundering).</summary>
    public static Island? ShipyardFrom(World world, Vector2 position)
    {
        Island? nearest = null;
        var nearestDistance = Plundering.Range;
        foreach (var island in world.Islands)
        {
            if (!island.HasShipyard)
                continue;
            var distance = island.DistanceTo(position);
            if (distance <= nearestDistance)
            {
                nearest = island;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    /// <summary>A ship can trade with a shipyard only while its anchor is down beside one.</summary>
    public static Island? DockedAt(World world, Ship ship) =>
        ship.Anchor == AnchorState.Down ? ShipyardFrom(world, ship.Position) : null;

    public static int Level(Ship ship, UpgradeDefinition upgrade) => ship.ModifierCount(upgrade.Source);

    public static PurchaseResult TryPurchase(World world, Ship ship, string upgradeId)
    {
        if (ship.OwnerPlayerId is not { } playerId || DockedAt(world, ship) is null)
            return PurchaseResult.NotAtShipyard;

        var upgrade = UpgradeCatalog.Find(upgradeId);
        if (upgrade is null)
            return PurchaseResult.UnknownUpgrade;

        var level = Level(ship, upgrade);
        if (level >= upgrade.MaxLevel)
            return PurchaseResult.MaxLevel;

        var player = world.GetOrAddPlayer(playerId);
        var cost = upgrade.CostAt(level);
        if (player.Gold < cost)
            return PurchaseResult.NotEnoughGold;

        world.AddGold(playerId, -cost);
        ship.AddModifier(upgrade.Modifier);
        world.Emit(new UpgradePurchased(world.Tick, ship.Id, upgrade.Id, level + 1));
        return PurchaseResult.Purchased;
    }

    /// <summary>
    /// Buys a locked weapon from <see cref="WeaponCatalog"/>: it goes on the next free slot's key for the rest of the
    /// run. Null on success.
    /// </summary>
    public static RejectionReason? TryUnlockAbility(World world, Ship ship, string abilityId)
    {
        if (ship.OwnerPlayerId is not { } playerId || DockedAt(world, ship) is null)
            return RejectionReason.NotAtShipyard;
        if (WeaponCatalog.Find(abilityId) is not { } weapon)
            return RejectionReason.UnknownUpgrade;
        if (ship.HasAbility(abilityId))
            return RejectionReason.AlreadyOwned;
        if (ship.FreeAbilitySlot is not { } slot)
            return RejectionReason.NoFreeSlot;
        if (world.GetOrAddPlayer(playerId).Gold < weapon.UnlockCost)
            return RejectionReason.NotEnoughGold;

        world.AddGold(playerId, -weapon.UnlockCost);
        ship.SetAbility(slot, weapon.Ability);
        world.Emit(new AbilityUnlocked(world.Tick, ship.Id, abilityId, slot));
        return null;
    }

    /// <summary>Where <paramref name="skill"/> stands for <paramref name="ship"/>: owned, buyable, or why not.</summary>
    public static SkillStatus StatusOf(Ship ship, SkillDefinition skill)
    {
        if (ship.HasSkill(skill.Id))
            return SkillStatus.Owned;
        if (!ship.HasAbility(skill.AbilityId))
            return SkillStatus.WeaponLocked;
        if (IsClosedOff(ship, skill))
            return SkillStatus.Excluded;
        var hasAll = skill.Requires.All(ship.HasSkill);
        var hasAny = skill.RequiresAny.Count == 0 || skill.RequiresAny.Any(ship.HasSkill);
        return hasAll && hasAny ? SkillStatus.Available : SkillStatus.NeedsPrerequisite;
    }

    /// <summary>Whether the ship's choices so far rule a skill out for good (see <see cref="SkillTrees.IsClosedOff"/>).</summary>
    public static bool IsClosedOff(Ship ship, SkillDefinition skill) =>
        SkillTrees.IsClosedOff(ship.Skills.Select(s => s.Id).ToList(), skill);

    /// <summary>Buys a skill from one of the ship's weapons' trees (see <see cref="SkillTrees"/>). Null on success.</summary>
    public static RejectionReason? TryPurchaseSkill(World world, Ship ship, string skillId)
    {
        if (ship.OwnerPlayerId is not { } playerId || DockedAt(world, ship) is null)
            return RejectionReason.NotAtShipyard;
        if (SkillTrees.Find(skillId) is not { } skill)
            return RejectionReason.UnknownUpgrade;

        switch (StatusOf(ship, skill))
        {
            case SkillStatus.Owned: return RejectionReason.AlreadyOwned;
            case SkillStatus.WeaponLocked: return RejectionReason.AbilityLocked;
            case SkillStatus.Excluded: return RejectionReason.ExcludedByChoice;
            case SkillStatus.NeedsPrerequisite: return RejectionReason.MissingPrerequisite;
        }
        if (world.GetOrAddPlayer(playerId).Gold < skill.Cost)
            return RejectionReason.NotEnoughGold;

        world.AddGold(playerId, -skill.Cost);
        ship.AddSkill(skill);
        world.Emit(new SkillPurchased(world.Tick, ship.Id, skill.Id));
        return null;
    }

    /// <summary>Asks to plunder the shipyard we're anchored at (shipyards don't plunder unless asked). Null on success.</summary>
    public static RejectionReason? TryChoosePlunder(World world, Ship ship)
    {
        var shipyard = DockedAt(world, ship);
        if (shipyard is null)
            return RejectionReason.NotAtShipyard;
        if (world.PlunderCooldownTicks(shipyard) > 0)
            return RejectionReason.IslandOnCooldown;
        ship.PlunderConsentIslandId = shipyard.Id;
        return null;
    }

    public static RejectionReason? ToRejection(PurchaseResult result) => result switch
    {
        PurchaseResult.Purchased => null,
        PurchaseResult.NotAtShipyard => RejectionReason.NotAtShipyard,
        PurchaseResult.UnknownUpgrade => RejectionReason.UnknownUpgrade,
        PurchaseResult.MaxLevel => RejectionReason.MaxLevel,
        _ => RejectionReason.NotEnoughGold,
    };
}
