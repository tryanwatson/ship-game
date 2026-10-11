using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Tests;

/// <summary>
/// Whole voyages with a crew voting at random, at random moments, for any stop at all (many of them not on offer), and
/// sometimes sinking: however it's played, the crew only ever sails one step along the chart at a time.
/// </summary>
public class VoyageFuzzTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(4, 9)]
    public void TheCrew_OnlyEverSailsOneStepAlongTheChart(int seed, int players)
    {
        var rng = new Random(seed);
        var world = Runs.Create(seed, Enumerable.Range(1, players).Select(id => (id, $"SAILOR {id}")).ToList());
        var director = world.Director!;
        var chart = director.Chart!;
        var visited = new List<int> { director.NodeId };

        for (var tick = 0; tick < 60_000 && !world.IsRunOver; tick++)
        {
            foreach (var player in world.Players.Values)
            {
                if (player.CardOffers.Count > 0 && rng.Next(10) == 0)
                    world.Enqueue(new ChooseCardCommand(player.PlayerId, player.CardOffers[0].Cards[rng.Next(player.CardOffers[0].Cards.Count)].Id));
                else if (player.NeedsStartingWeapon && player.CardOffers.Count == 0)
                    world.Enqueue(new ChooseStartingWeaponCommand(player.PlayerId, LongGun.AbilityId));
                if (rng.Next(40) == 0)
                {
                    // Mostly where the chart leads; sometimes anywhere at all.
                    var options = director.CurrentNode!.Next;
                    var pick = options.Count > 0 && rng.Next(3) > 0 ? options[rng.Next(options.Count)] : chart.Nodes[rng.Next(chart.Nodes.Count)].Id;
                    world.Enqueue(new ChooseCourseCommand(player.PlayerId, pick));
                }
            }
            if (rng.Next(200) == 0)
            {
                // Speed things up: sink the forts or the boss; now and then a crewmate goes down too.
                foreach (var ship in world.Ships.Where(s => s.IsFort || s.IsBoss))
                    ship.Health = 0f;
                if (rng.Next(4) == 0 && world.Ships.FirstOrDefault(s => s.OwnerPlayerId is not null) is { } unlucky && world.Ships.Count(s => s.OwnerPlayerId is not null) > 1)
                    unlucky.Health = 0f;
            }
            foreach (var ship in world.Ships.Where(s => s.OwnerPlayerId is not null && s.Stats.MaxHealth < 1000f))
            {
                ship.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 1_000_000f, "fuzz"));
                ship.Health = ship.Stats.MaxHealth;
            }

            var before = director.NodeId;
            world.Step();
            if (director.NodeId != before)
            {
                Assert.True(chart.Leads(before, director.NodeId), $"sailed {before} -> {director.NodeId}, which the chart doesn't lead");
                visited.Add(director.NodeId);
            }
        }

        Assert.Equal(visited, director.Route);
        Assert.True(world.IsVictory, $"the voyage stalled at {director.CurrentNode} after {visited.Count} stops");
        // Start, three rows and a boss an act: never more, never fewer.
        Assert.Equal(1 + SeaChart.Acts * (SeaChart.RowsPerAct + 1), visited.Count);
    }
}
