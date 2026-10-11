using System.Numerics;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

public class RegionTests
{
    /// <summary>Every stop of a good few charts, built as its region.</summary>
    public static IEnumerable<(ChartNode Node, RegionLayout Layout)> Regions_(int charts = 15) =>
        Enumerable.Range(0, charts).SelectMany(seed => SeaChart.Generate(seed).Nodes.Select(n => (n, Regions.Build(n, seed, firstIslandId: 1))));

    [Fact]
    public void EveryRegion_KeepsItsIslandsInside_ApartFromEachOther_AndClearOfTheEntry()
    {
        foreach (var (node, layout) in Regions_())
        {
            Assert.Equal(Regions.SizeFor(node.Kind), layout.Size);
            Assert.Equal(new Vector2(layout.Size.X / 2f, layout.Size.Y - Regions.EntryOffing), layout.Entry);
            Assert.NotEmpty(layout.Islands);
            Assert.Equal(layout.Islands.Count, layout.Islands.Select(i => i.Id).Distinct().Count());
            Assert.Equal(layout.Islands.Count, layout.Islands.Select(i => i.Name).Distinct().Count());
            foreach (var island in layout.Islands)
            {
                foreach (var point in island.Outline)
                    Assert.True(point.X > 0 && point.Y > 0 && point.X < layout.Size.X && point.Y < layout.Size.Y, $"{island.Name} runs off the map");
                // A full crew comes in abreast: every one of them in open water.
                for (var i = 0; i < Regions.MaxCrew; i++)
                {
                    var berth = layout.Entry + new Vector2((i - (Regions.MaxCrew - 1) / 2f) * Progression.Runs.StartSpacing, 0f);
                    Assert.True(island.DistanceTo(berth) > 12f, $"{island.Name} crowds sailor {i + 1} of a full crew at {node.Kind}");
                }
                Assert.All(layout.Islands.Where(other => other != island), other =>
                    Assert.True(Vector2.Distance(island.Center, other.Center) > island.BoundingRadius + other.BoundingRadius, $"{island.Name} touches {other.Name}"));
            }
        }
    }

    [Fact]
    public void EveryFortressAndPort_IsNamedOnTheChart_WithNoWordRepeatedInTheRun()
    {
        foreach (var seed in Enumerable.Range(0, 30))
        {
            var named = SeaChart.Generate(seed).Nodes.Where(n => n.Kind is NodeKind.Fortress or NodeKind.Port).ToList();
            Assert.All(named, n => Assert.InRange(n.Name.Length, 1, IslandNames.MaxLength));
            // The place's own word ("GULL" of "GULL WATCH", "MERROW" of "PORT MERROW") is never used twice.
            var words = named.Select(n => n.Name.Replace("PORT ", "").Replace("FORT ", "").Split(' ')[0]).ToList();
            Assert.Equal(words.Count, words.Distinct().Count());
        }

        // And the island the crew finds there is the one on the chart, among islands named after nothing like it.
        foreach (var (node, layout) in Regions_().Where(r => r.Layout.Objective is not null))
        {
            var objective = layout.Islands.Single(i => i.Id == layout.Objective);
            Assert.Equal(node.Name, objective.Name);
            var words = layout.Islands.SelectMany(i => i.Name.Split(' ')).ToList();
            Assert.Equal(words.Count, words.Distinct().Count());
        }
    }

    [Fact]
    public void AFortressOrPort_StandsNorthOfTheEntry_AtTheStopsLevel()
    {
        foreach (var (node, layout) in Regions_())
        {
            var fortresses = layout.Islands.Where(i => i.IsFortress).ToList();
            var yards = layout.Islands.Where(i => i.HasShipyard).ToList();
            switch (node.Kind)
            {
                case NodeKind.Fortress:
                    var fortress = Assert.Single(fortresses);
                    Assert.Empty(yards);
                    Assert.Equal((fortress.Id, node.Level), (layout.Objective, fortress.Level));
                    Assert.True(fortress.Center.Y < layout.Entry.Y - 60f, "a fortress should be a sail away");
                    Assert.Equal(Regions.FortressArea(node.Level), fortress.Area, 1);
                    break;
                case NodeKind.Port:
                    Assert.Equal(Assert.Single(yards).Id, layout.Objective);
                    Assert.Empty(fortresses);
                    break;
                default:
                    Assert.Empty(fortresses);
                    Assert.Empty(yards);
                    Assert.Null(layout.Objective);
                    break;
            }
            Assert.All(layout.Islands, i => Assert.Equal(node.Level, i.Level));
        }
    }

    [Fact]
    public void ARegion_IsTheSameForTheSameSeed_AndNumbersItsIslandsFromWhereItsTold()
    {
        var node = SeaChart.Generate(2).Nodes.First(n => n.Kind == NodeKind.Boss);
        var one = Regions.Build(node, seed: 9, firstIslandId: 50);
        var again = Regions.Build(node, seed: 9, firstIslandId: 50);
        var other = Regions.Build(node, seed: 10, firstIslandId: 50);

        Assert.Equal(one.Islands.Select(i => (i.Id, i.Center)), again.Islands.Select(i => (i.Id, i.Center)));
        Assert.NotEqual(one.Islands.Select(i => i.Center), other.Islands.Select(i => i.Center));
        Assert.Equal(Enumerable.Range(50, one.Islands.Count), one.Islands.Select(i => i.Id));
    }
}
