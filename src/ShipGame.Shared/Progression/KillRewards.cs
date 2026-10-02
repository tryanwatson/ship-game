using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Progression;

/// <summary>What a player earns for sinking an enemy ship.</summary>
public static class KillRewards
{
    public const int Gold = 5;
    public const float SpeedBonus = 0.05f;
    public const float CooldownSpeedBonus = 0.05f;

    public const string Source = "kill-reward";

    public static void Grant(World world, Ship killer)
    {
        if (killer.OwnerPlayerId is not { } playerId)
            return; // NPC kills earn nothing

        var player = world.GetOrAddPlayer(playerId);
        player.Gold += Gold;
        player.Kills++;

        // Gold is the player's; the rest goes to the ship, and a ship that went down in the same exchange stays down.
        if (killer.IsSunk)
            return;

        killer.AddModifier(new StatModifier(StatId.MaxSpeed, ModifierKind.Percent, SpeedBonus, Source));
        killer.AddModifier(new StatModifier(StatId.CooldownSpeed, ModifierKind.Percent, CooldownSpeedBonus, Source));
        killer.Health = killer.Stats.MaxHealth;
    }
}
