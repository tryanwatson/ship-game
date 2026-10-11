using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Progression;

/// <summary>
/// A pirate's level: the level of the stop on the chart it sails at (the fortress it serves, the act's flagship), and so
/// how dangerous it is and how much it's worth. Each level past the first adds to its hull, guns, reload, and speed, and
/// to the gold for sinking it.
/// </summary>
public static class PirateLevels
{
    // Per level after the first, as fractions of base stats.
    public const float HealthPerLevel = 0.25f;
    public const float DamagePerLevel = 0.13f;
    public const float CooldownSpeedPerLevel = 0.04f;
    public const float SpeedPerLevel = 0.02f;

    public const string Source = "pirate-level";

    /// <summary>Sets <paramref name="pirate"/>'s level and the stat bonuses that go with it (replacing any it had).</summary>
    public static void Apply(Ship pirate, int level)
    {
        pirate.Level = Math.Max(1, level);
        pirate.RemoveModifiers(Source);
        var scale = pirate.Level - 1;
        if (scale > 0)
        {
            pirate.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Percent, HealthPerLevel * scale, Source));
            pirate.AddModifier(new StatModifier(StatId.WeaponDamage, ModifierKind.Percent, DamagePerLevel * scale, Source));
            pirate.AddModifier(new StatModifier(StatId.CooldownSpeed, ModifierKind.Percent, CooldownSpeedPerLevel * scale, Source));
            pirate.AddModifier(new StatModifier(StatId.MaxSpeed, ModifierKind.Percent, SpeedPerLevel * scale, Source));
        }
        pirate.Health = pirate.Stats.MaxHealth;
    }

    /// <summary>Gold for sinking a ship of <paramref name="level"/>; ships without a level (players) pay as level 1.</summary>
    public static int KillGold(int level) => KillRewards.Gold * Math.Max(1, level);

    /// <summary>Gold for plundering an island of <paramref name="level"/>.</summary>
    public static int PlunderGold(int level) => Island.DefaultPlunderGold * Math.Max(1, level);
}
