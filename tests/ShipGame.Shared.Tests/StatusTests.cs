using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

/// <summary>What the status tooltips say: every status explains itself, and its numbers match what it does.</summary>
public class StatusTests
{
    [Fact]
    public void EveryStatusIsDefinedAndExplained()
    {
        foreach (var id in Enum.GetValues<StatusId>())
        {
            var definition = Statuses.Get(id);
            Assert.False(string.IsNullOrWhiteSpace(definition.Summary), $"{id} has no summary");
            Assert.False(string.IsNullOrWhiteSpace(Statuses.Effect(id, 1, 0.1f)), $"{id} has no effect line");
            Assert.Equal(definition.Summary.ToUpperInvariant(), definition.Summary); // the pixel font is capitals only
        }
    }

    [Theory]
    [InlineData(StatusId.Burning, 3, 4f, "TAKING 12 DAMAGE A SECOND.")]
    [InlineData(StatusId.Frenzy, 4, 0.1f, "+40% RELOAD SPEED, +20% SPEED.")]
    [InlineData(StatusId.Marked, 1, 0.25f, "TAKING +25% DAMAGE.")]
    [InlineData(StatusId.Slowed, 1, 0.3f, "-30% SPEED.")]
    [InlineData(StatusId.Entrenched, 5, 0.04f, "+20% DAMAGE WITH EVERY WEAPON.")]
    public void EffectLineCountsEveryStack(StatusId id, int stacks, float power, string expected)
    {
        Assert.Equal(expected, Statuses.Effect(id, stacks, power));
    }
}
