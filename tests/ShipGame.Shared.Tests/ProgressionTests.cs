using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Tests;

public class ProgressionTests
{
    private const int PlayerId = 1;

    private static (World world, Ship player) CreateWorld()
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(30, 30), 0f, ShipStats.Sloop, PlayerId, Loadouts.FullArsenal);
        return (world, player);
    }

    /// <summary>Spawns an enemy off the player's starboard beam, one volley from sinking, and sinks it.</summary>
    private static Ship SinkEnemy(World world, Ship player)
    {
        var enemy = world.SpawnShip(player.Position + new Vector2(0, 4), 0f, ShipStats.Sloop);
        enemy.Health = 1f;
        // Wait out any cooldown left from a previous kill.
        for (var t = 0; t < SimConstants.TickRate * 5 && !player.GetAbility(AbilitySlot.One)!.IsReady; t++)
            world.Step();
        world.TryCastAbility(player, AbilitySlot.One, player.Position + new Vector2(0, 5));
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
    public void Kill_GrantsGold_AndNothingElse()
    {
        var (world, player) = CreateWorld();
        player.Health = 30f;
        var statsBefore = player.Stats;

        SinkEnemy(world, player);

        Assert.Equal(KillRewards.Gold, world.Players[PlayerId].Gold);
        Assert.Equal(1, world.Players[PlayerId].Kills);
        Assert.Equal(statsBefore, player.Stats);              // no permanent bonus
        Assert.InRange(player.Health, 30f, 31f);              // and no heal, beyond a moment's regeneration
    }

    [Fact]
    public void Ships_RegenerateHealthEverySecond_UpToTheirMaximum()
    {
        var (world, player) = CreateWorld();
        player.Health = 50f;

        for (var i = 0; i < SimConstants.TickRate * 4; i++)
            world.Step();
        Assert.Equal(50f + 4 * ShipStats.Sloop.HealthRegen, player.Health, 2);

        player.Health = player.Stats.MaxHealth - 0.01f;
        world.Step();
        world.Step();
        Assert.Equal(player.Stats.MaxHealth, player.Health);
    }

    [Fact]
    public void RepairsUpgrade_RaisesRegeneration()
    {
        var (world, player) = CreateWorld();
        var repairs = UpgradeCatalog.Find("repairs")!;
        player.AddModifier(repairs.Modifier);
        player.AddModifier(repairs.Modifier);
        player.Health = 50f;

        for (var i = 0; i < SimConstants.TickRate; i++)
            world.Step();

        Assert.Equal(ShipStats.Sloop.HealthRegen + 2 * repairs.ValuePerLevel, player.Stats.HealthRegen, 4);
        Assert.Equal(50f + player.Stats.HealthRegen, player.Health, 2);
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
        Assert.Equal(1f, volley.CooldownFraction(BroadsideVolley.PortChannel));
        for (var t = 0; t < expected; t++)
            world.Step();
        Assert.True(volley.IsChannelReady(BroadsideVolley.PortChannel));
    }

    [Fact]
    public void NpcKills_EarnNothing()
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        var pirate = world.SpawnShip(new Vector2(30, 30), 0f, ShipStats.Sloop, abilities: Loadouts.FullArsenal);
        var player = world.SpawnShip(new Vector2(30, 34), 0f, ShipStats.Sloop, PlayerId);
        player.Health = 1f;

        world.TryCastAbility(pirate, AbilitySlot.One, pirate.Position + new Vector2(0, 5));
        for (var t = 0; t < SimConstants.TickRate; t++)
            world.Step();

        Assert.DoesNotContain(player, world.Ships);
        Assert.Equal(0, world.Players[PlayerId].Gold);
        Assert.Empty(pirate.Modifiers);
    }
}
