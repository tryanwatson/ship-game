using ShipGame.Shared.Simulation;

using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Progression;

/// <summary>
/// What players earn for sinking an enemy ship: gold, more for higher levels, split evenly between whoever sank it
/// and every other player who hit it in the last <see cref="AssistSeconds"/>. The kill itself counts for the sinker.
/// </summary>
public static class KillRewards
{
    /// <summary>Gold for a level 1 kill; higher levels pay more (see <see cref="PirateLevels.KillGold"/>).</summary>
    public const int Gold = 5;

    /// <summary>A fort pays this many times a ship of its level: it's a bigger prize.</summary>
    public const int FortMultiplier = 3;

    /// <summary>A boss pays this many times a ship of its level.</summary>
    public const int BossMultiplier = 10;

    /// <summary>A hit this recent before the sinking earns a share of the gold.</summary>
    public const float AssistSeconds = 15f;
    public static readonly int AssistTicks = (int)(AssistSeconds * SimConstants.TickRate);

    public static void Grant(World world, Ship killer, Ship victim)
    {
        if (killer.OwnerPlayerId is not { } playerId)
            return; // NPC kills earn nothing

        GoldShares.Pay(world, GoldFor(victim), playerId, Assists(world, victim, playerId));
        world.GetOrAddPlayer(playerId).Kills++;

        // A prize crew patches up the ship from what's taken.
        if (!killer.IsSunk && killer.PerkValue(Upgrades.Perk.PrizeCrewHeal) is > 0f and var heal)
            killer.Health = MathF.Min(killer.Stats.MaxHealth, killer.Health + heal * killer.Stats.MaxHealth);
    }

    /// <summary>
    /// The gold for sinking <paramref name="victim"/>, before it's shared out. A fort or boss made sturdier for a bigger
    /// crew (see <see cref="Fortresses.CrewSizeSource"/>) pays that much more, so there's as much to share round.
    /// </summary>
    public static int GoldFor(Ship victim)
    {
        var gold = PirateLevels.KillGold(victim.Level) * (victim.IsBoss ? BossMultiplier : victim.IsFort ? FortMultiplier : 1);
        var crew = victim.Modifiers.Where(m => m.Source == Fortresses.CrewSizeSource && m.Kind == ModifierKind.Multiplier)
            .Select(m => m.Value).DefaultIfEmpty(1f).Max();
        return (int)MathF.Round(gold * crew);
    }

    /// <summary>The players other than <paramref name="killerId"/> who hit <paramref name="victim"/> recently enough to share.</summary>
    public static IEnumerable<int> Assists(World world, Ship victim, int killerId) =>
        victim.PlayerHits.Where(h => h.Key != killerId && world.Tick - h.Value <= AssistTicks).Select(h => h.Key);
}
