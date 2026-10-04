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
            var gold = share + (i < remainder ? 1 : 0);
            if (gold > 0)
                world.AddGold(crew[i], gold);
        }
    }
}
