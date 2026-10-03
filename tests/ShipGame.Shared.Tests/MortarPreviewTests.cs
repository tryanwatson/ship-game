using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Tests;

public class MortarPreviewTests
{
    [Theory]
    [InlineData("", 0f)]
    [InlineData("", 1.2f)]
    [InlineData("bombardment", 0f)]
    [InlineData("bombardment", 1.2f)]
    [InlineData("cluster-shell", 0f)]
    [InlineData("cluster-shell", 1.2f)]
    public void PreviewLandingPoints_MatchLaunchedShells_WithRangeClampAndSkills(string branch, float heading)
    {
        var world = new World(new Vector2(256, 256));
        var ship = world.SpawnShip(new Vector2(100, 100), heading, ShipStats.Sloop, 1,
            Loadouts.Starting(WeaponCatalog.Mortar.Ability));
        foreach (var id in branch switch
        {
            "bombardment" => new[] { "quick-fuse", "bombardment", "siege-artillery" },
            "cluster-shell" => new[] { "heavy-shell", "cluster-shell", "siege-artillery" },
            _ => Array.Empty<string>(),
        })
            ship.AddSkill(SkillTrees.Find(id)!);

        var aim = ship.Position + new Vector2(100, 80);
        var predicted = Enumerable.Range(0, Mortar.ShellCountFor(ship))
            .Select(i => Mortar.ShellLandingPoint(ship, aim, i)).ToArray();
        Assert.True(world.TryCastAbility(ship, AbilitySlot.One, aim));

        var launches = world.DrainEvents().OfType<AreaStrikeLaunched>().ToArray();
        Assert.Equal(predicted.Length, launches.Length);
        for (var i = 0; i < launches.Length; i++)
        {
            Assert.Equal(predicted[i], launches[i].Target);
            Assert.Equal(Mortar.BlastRadiusFor(ship), launches[i].Radius);
            Assert.Equal(Mortar.FlightTicks(ship, Mortar.RangeFor(ship)) + i * Mortar.SalvoGapTicks,
                launches[i].ImpactTick - launches[i].Tick);
        }
        Assert.Equal(Mortar.RangeFor(ship), Vector2.Distance(ship.Position, launches[0].Target), 3);
    }
}
