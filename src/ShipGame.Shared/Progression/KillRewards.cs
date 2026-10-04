using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Progression;

/// <summary>What a player earns for sinking an enemy ship: gold, more for higher levels. Nothing else.</summary>
public static class KillRewards
{
    /// <summary>Gold for a level 1 kill; higher levels pay more (see <see cref="PirateLevels.KillGold"/>).</summary>
    public const int Gold = 5;

    public static void Grant(World world, Ship killer, Ship victim)
    {
        if (killer.OwnerPlayerId is not { } playerId)
            return; // NPC kills earn nothing

        world.AddGold(playerId, PirateLevels.KillGold(victim.Level));
        world.GetOrAddPlayer(playerId).Kills++;
    }
}
