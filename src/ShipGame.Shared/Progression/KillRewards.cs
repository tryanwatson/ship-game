using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Progression;

/// <summary>
/// What players earn for sinking an enemy ship: gold, more for higher levels, split evenly between whoever sank it
/// and every other player who hit it in the last <see cref="AssistSeconds"/>. The kill itself counts for the sinker.
/// </summary>
public static class KillRewards
{
    /// <summary>Gold for a level 1 kill; higher levels pay more (see <see cref="PirateLevels.KillGold"/>).</summary>
    public const int Gold = 5;

    /// <summary>A hit this recent before the sinking earns a share of the gold.</summary>
    public const float AssistSeconds = 15f;
    public static readonly int AssistTicks = (int)(AssistSeconds * SimConstants.TickRate);

    public static void Grant(World world, Ship killer, Ship victim)
    {
        if (killer.OwnerPlayerId is not { } playerId)
            return; // NPC kills earn nothing

        GoldShares.Pay(world, PirateLevels.KillGold(victim.Level), playerId, Assists(world, victim, playerId));
        world.GetOrAddPlayer(playerId).Kills++;
    }

    /// <summary>The players other than <paramref name="killerId"/> who hit <paramref name="victim"/> recently enough to share.</summary>
    public static IEnumerable<int> Assists(World world, Ship victim, int killerId) =>
        victim.PlayerHits.Where(h => h.Key != killerId && world.Tick - h.Value <= AssistTicks).Select(h => h.Key);
}
