using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Tests;

public class ProgressionTests
{
    private const int PlayerId = 1;

    private static (World world, Ship player) CreateWorld()
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(30, 30), 0f, ShipStats.Sloop, PlayerId, Loadouts.Sloop);
        return (world, player);
    }

    /// <summary>Spawns an enemy off the player's starboard beam, one volley from sinking, and sinks it.</summary>
    private static Ship SinkEnemy(World world, Ship player)
    {
        var enemy = world.SpawnShip(player.Position + new Vector2(0, 4), 0f, ShipStats.Sloop);
        enemy.Health = 1f;
        // Wait out any cooldown left from a previous kill.
        for (var t = 0; t < SimConstants.TickRate * 5 && !player.GetAbility(AbilitySlot.Two)!.IsReady; t++)
            world.Step();
        world.TryCastAbility(player, AbilitySlot.Two, Vector2.Zero);
        for (var t = 0; t < SimConstants.TickRate && world.Ships.Contains(enemy); t++)
            world.Step();
        Assert.DoesNotContain(enemy, world.Ships);
        return enemy;
    }

    [Fact]
    public void StatModifiers_ApplyFlatThenPercent()
    {
        var modifiers = new StatModifiers();
        modifiers.Add(new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 20f, "a"));
        modifiers.Add(new StatModifier(StatId.MaxHealth, ModifierKind.Percent, 0.10f, "b"));
        modifiers.Add(new StatModifier(StatId.MaxHealth, ModifierKind.Percent, 0.15f, "c"));

        Assert.Equal((100f + 20f) * 1.25f, modifiers.Apply(StatId.MaxHealth, 100f), 3);
        Assert.Equal(5f, modifiers.Apply(StatId.MaxSpeed, 5f)); // other stats untouched
    }

    [Fact]
    public void StatModifiers_RemoveSourceUndoesOnlyThatSource()
    {
        var modifiers = new StatModifiers();
        modifiers.Add(new StatModifier(StatId.MaxSpeed, ModifierKind.Percent, 0.05f, "kill"));
        modifiers.Add(new StatModifier(StatId.MaxSpeed, ModifierKind.Percent, 0.05f, "kill"));
        modifiers.Add(new StatModifier(StatId.MaxSpeed, ModifierKind.Flat, 1f, "item"));

        Assert.Equal(2, modifiers.RemoveSource("kill"));
        Assert.Equal(6f, modifiers.Apply(StatId.MaxSpeed, 5f), 3);
    }

    [Fact]
    public void RaisingMaxHealth_RaisesCurrentHealthBySameAmount()
    {
        var (_, player) = CreateWorld();
        player.Health = 40f;

        player.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 25f, "item"));
        Assert.Equal(125f, player.Stats.MaxHealth);
        Assert.Equal(65f, player.Health);

        player.RemoveModifiers("item");
        Assert.Equal(100f, player.Stats.MaxHealth);
        Assert.Equal(65f, player.Health); // only clamps when lowered
    }

    [Fact]
    public void Kill_GrantsGoldFullHealAndBonuses()
    {
        var (world, player) = CreateWorld();
        player.Health = 30f;

        SinkEnemy(world, player);

        Assert.Equal(KillRewards.Gold, world.Players[PlayerId].Gold);
        Assert.Equal(1, world.Players[PlayerId].Kills);
        Assert.Equal(player.Stats.MaxHealth, player.Health);
        Assert.Equal(ShipStats.Sloop.MaxSpeed * 1.05f, player.Stats.MaxSpeed, 4);
        Assert.Equal(1.05f, player.Stats.CooldownSpeed, 4);
    }

    [Fact]
    public void KillBonuses_StackAdditively()
    {
        var (world, player) = CreateWorld();

        for (var i = 0; i < 3; i++)
            SinkEnemy(world, player);

        Assert.Equal(3 * KillRewards.Gold, world.Players[PlayerId].Gold);
        Assert.Equal(ShipStats.Sloop.MaxSpeed * 1.15f, player.Stats.MaxSpeed, 4);
        Assert.Equal(1.15f, player.Stats.CooldownSpeed, 4);
    }

    [Fact]
    public void FasterCooldownSpeed_ShortensCooldowns()
    {
        var (world, player) = CreateWorld();
        player.AddModifier(new StatModifier(StatId.CooldownSpeed, ModifierKind.Percent, 0.25f, "test"));

        world.TryCastAbility(player, AbilitySlot.One, Vector2.Zero);
        var volley = player.GetAbility(AbilitySlot.One)!;

        var expected = (int)MathF.Round(volley.Definition.CooldownTicks / 1.25f);
        Assert.Equal(expected, volley.CooldownDurationTicks);
        Assert.Equal(1f, volley.CooldownFraction);
        for (var t = 0; t < expected; t++)
            world.Step();
        Assert.True(volley.IsReady);
    }

    [Fact]
    public void NpcKills_EarnNothing()
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        var pirate = world.SpawnShip(new Vector2(30, 30), 0f, ShipStats.Sloop, abilities: Loadouts.Sloop);
        var player = world.SpawnShip(new Vector2(30, 34), 0f, ShipStats.Sloop, PlayerId);
        player.Health = 1f;

        world.TryCastAbility(pirate, AbilitySlot.Two, Vector2.Zero);
        for (var t = 0; t < SimConstants.TickRate; t++)
            world.Step();

        Assert.DoesNotContain(player, world.Ships);
        Assert.Equal(0, world.Players[PlayerId].Gold);
        Assert.Empty(pirate.Modifiers);
    }
}
