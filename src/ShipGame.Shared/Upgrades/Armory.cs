using ShipGame.Shared.Abilities;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Upgrades;

/// <summary>Gifts of kit (the Free Armory card): what a port would sell, for nothing.</summary>
public static class Armory
{
    /// <summary>
    /// A random weapon the player's ship lacks, on its next free slot; with every weapon aboard (or no slot free), a
    /// random skill it could buy instead. A player waiting to respawn gets it on the ship they'll sail next.
    /// </summary>
    public static void GiveSomething(World world, PlayerState player, Random rng)
    {
        var ship = world.GetPlayerShip(player.PlayerId) ?? player.LostShip;
        if (ship is null)
            return;

        var missing = WeaponCatalog.All.Where(w => !ship.HasAbility(w.Id)).ToList();
        if (missing.Count > 0 && ship.FreeAbilitySlot is { } slot)
        {
            var weapon = missing[rng.Next(missing.Count)];
            ship.SetAbility(slot, weapon.Ability);
            world.Emit(new AbilityUnlocked(world.Tick, ship.Id, weapon.Id, slot));
            return;
        }

        var skills = SkillTrees.All.Where(s => Shipyards.StatusOf(ship, s) == SkillStatus.Available).ToList();
        if (skills.Count == 0)
            return;
        var skill = skills[rng.Next(skills.Count)];
        ship.AddSkill(skill);
        world.Emit(new SkillPurchased(world.Tick, ship.Id, skill.Id));
    }
}
