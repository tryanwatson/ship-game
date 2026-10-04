using ShipGame.Shared.Abilities;
using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Upgrades;

/// <param name="UnlockCost">Gold to buy it at a shipyard when it isn't the weapon the run started with.</param>
/// <param name="StartingBonus">
/// What choosing it as the starting weapon does for the ship, for the whole run (it isn't granted when the weapon's
/// bought later). Each weapon's suits how it fights: the broadside closes in, so its ship is tougher.
/// </param>
/// <param name="StartingBonusText">The bonus in a few words, for the weapon choice.</param>
public sealed record WeaponOffer(Ability Ability, int UnlockCost, IReadOnlyList<StatModifier> StartingBonus, string StartingBonusText)
{
    public string Id => Ability.Id;
}

/// <summary>
/// The weapons a player ship can carry. Each run starts with one of them, chosen before the run, free, with its
/// <see cref="WeaponOffer.StartingBonus"/>; the rest are bought at shipyards and take the next free ability slot. Add a
/// weapon by adding a row (and its skill tree in <see cref="SkillTrees"/>).
/// </summary>
public static class WeaponCatalog
{
    /// <summary>The source tag on a starting weapon's bonus modifiers.</summary>
    public const string StartingBonusSource = "starting-weapon";

    public static readonly WeaponOffer Broadside = new(new BroadsideVolley(), UnlockCost: 45,
        new[]
        {
            new StatModifier(StatId.MaxHealth, ModifierKind.Percent, 0.5f, StartingBonusSource),
            new StatModifier(StatId.HealthRegen, ModifierKind.Flat, 2f, StartingBonusSource),
        },
        "+50% MAX HEALTH AND +2 HEALTH A SECOND");

    public static readonly WeaponOffer LongGun = new(new LongGun(), UnlockCost: 45,
        new[] { new StatModifier(StatId.WeaponRange, ModifierKind.Percent, 0.25f, StartingBonusSource) },
        "+25% RANGE ON EVERY WEAPON");

    public static readonly WeaponOffer Mortar = new(new Mortar(), UnlockCost: 45,
        new[] { new StatModifier(StatId.CooldownSpeed, ModifierKind.Percent, 0.25f, StartingBonusSource) },
        "+25% COOLDOWN SPEED ON EVERY WEAPON");

    public static readonly IReadOnlyList<WeaponOffer> All = new[] { Broadside, LongGun, Mortar };

    public static WeaponOffer? Find(string id) => All.FirstOrDefault(w => w.Id == id);
}
