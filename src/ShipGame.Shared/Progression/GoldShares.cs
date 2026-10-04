using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Progression;

/// <summary>Gold won together, split evenly. Coins that won't divide go one each to the leader first, then the rest.</summary>
public static class GoldShares
{
    /// <summary>Pays <paramref name="total"/> between <paramref name="leader"/> (who sank it, or plundered) and <paramref name="others"/>.</summary>
    public static void Pay(World world, int total, int leader, IEnumerable<int> others)
    {
        var crew = new List<int> { leader };
        crew.AddRange(others.Where(id => id != leader).Distinct().Order());
        var share = total / crew.Count;
        var remainder = total % crew.Count;
        for (var i = 0; i < crew.Count; i++)
        {
            // A privateer's share comes with a bonus on top, out of nobody else's.
            var bonus = world.GetPlayerShip(crew[i])?.PerkValue(Upgrades.Perk.GoldBonus) ?? 0f;
            var gold = (int)MathF.Round((share + (i < remainder ? 1 : 0)) * (1f + bonus));
            if (gold > 0)
                world.AddGold(crew[i], gold);
        }
    }
}
