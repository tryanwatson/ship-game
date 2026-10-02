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

        player.Gold -= cost;
        ship.AddModifier(upgrade.Modifier);
        return PurchaseResult.Purchased;
    }

    /// <summary>Asks to plunder the shipyard we're anchored at (shipyards don't plunder unless asked).</summary>
    public static bool TryChoosePlunder(World world, Ship ship)
    {
        var shipyard = DockedAt(world, ship);
        if (shipyard is null || world.PlunderCooldownTicks(shipyard) > 0)
            return false;
        ship.PlunderConsentIslandId = shipyard.Id;
        return true;
    }
}
