using ShipGame.Shared.Abilities;

namespace ShipGame.Shared.Upgrades;

/// <param name="UnlockCost">Gold to buy it at a shipyard when it isn't the weapon the run started with.</param>
public sealed record WeaponOffer(Ability Ability, int UnlockCost)
{
    public string Id => Ability.Id;
}

/// <summary>
/// The weapons a player ship can carry. Each run starts with one of them, chosen before the run, free; the rest are
/// bought at shipyards and take the next free ability slot. Add a weapon by adding a row (and its skill tree in
/// <see cref="SkillTrees"/>).
/// </summary>
public static class WeaponCatalog
{
    public static readonly WeaponOffer Broadside = new(new BroadsideVolley(), UnlockCost: 45);
    public static readonly WeaponOffer LongGun = new(new LongGun(), UnlockCost: 45);
    public static readonly WeaponOffer Mortar = new(new Mortar(), UnlockCost: 45);

    public static readonly IReadOnlyList<WeaponOffer> All = new[] { Broadside, LongGun, Mortar };

    public static WeaponOffer? Find(string id) => All.FirstOrDefault(w => w.Id == id);
}
