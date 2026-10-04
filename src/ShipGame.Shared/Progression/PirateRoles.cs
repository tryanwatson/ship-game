using ShipGame.Shared.Abilities;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Progression;

/// <summary>What a pirate fights with. Each carries one weapon, which gives it its name.</summary>
public enum PirateRole
{
    /// <summary>Broadside: closes in and trades volleys abeam.</summary>
    Buccaneer,

    /// <summary>Long gun: hangs back and picks you off.</summary>
    Sniper,

    /// <summary>Mortar: shells you from far off, over islands.</summary>
    Bomber,
}

/// <summary>
/// Pirate roles: their weapons and names. A ship's role isn't stored or sent anywhere; it's read off the weapon in its
/// first slot, so a client knows it from the loadout alone.
/// </summary>
public static class PirateRoles
{
    public static readonly IReadOnlyList<PirateRole> All = Enum.GetValues<PirateRole>();

    public static Ability Weapon(PirateRole role) => role switch
    {
        PirateRole.Sniper => WeaponCatalog.LongGun.Ability,
        PirateRole.Bomber => WeaponCatalog.Mortar.Ability,
        _ => WeaponCatalog.Broadside.Ability,
    };

    /// <summary>The role's weapon on 1, the other slots empty.</summary>
    public static IReadOnlyList<Ability?> Loadout(PirateRole role) => Loadouts.Starting(Weapon(role));

    public static string Name(PirateRole role) => role switch
    {
        PirateRole.Sniper => "Sniper",
        PirateRole.Bomber => "Bomber",
        _ => "Buccaneer",
    };

    /// <summary>Any role, with equal odds.</summary>
    public static PirateRole Pick(Random rng) => All[rng.Next(All.Count)];

    /// <summary>A pirate's role, by its first weapon; null for player ships and the flagship, which go by other names.</summary>
    public static PirateRole? Of(Ship ship)
    {
        if (ship.Team != Team.Pirates || ship.IsBoss)
            return null;
        return ship.Abilities[(int)AbilitySlot.One]?.Definition switch
        {
            BroadsideVolley => PirateRole.Buccaneer,
            LongGun => PirateRole.Sniper,
            Mortar => PirateRole.Bomber,
            _ => null,
        };
    }
}
