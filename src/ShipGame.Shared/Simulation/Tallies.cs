using ShipGame.Shared.Abilities;

namespace ShipGame.Shared.Simulation;

/// <summary>
/// Running counts a ship keeps of what it's done and had done to it (see <see cref="Ship.TallyOf"/>), for cards that
/// grow with them. A respawned ship takes over its lost one's.
/// </summary>
public enum Tally : byte
{
    BroadsideHits,
    LongGunHits,
    MortarHits,
    Kills,
    TilesSailed,
    DamageTaken,
    DamageDealt,
    GoldEarned,
    SecondsAnchored,
}

public static class Tallies
{
    /// <summary>What it counts, in the HUD's capitals: "NEXT IN 12 BROADSIDE HITS".</summary>
    public static string Noun(Tally tally) => tally switch
    {
        Tally.BroadsideHits => "BROADSIDE HITS",
        Tally.LongGunHits => "LONG GUN HITS",
        Tally.MortarHits => "MORTAR HITS",
        Tally.Kills => "KILLS",
        Tally.TilesSailed => "TILES SAILED",
        Tally.DamageTaken => "DAMAGE TAKEN",
        Tally.DamageDealt => "DAMAGE DEALT",
        Tally.GoldEarned => "GOLD EARNED",
        Tally.SecondsAnchored => "SECONDS AT ANCHOR",
        _ => tally.ToString().ToUpperInvariant(),
    };

    /// <summary>The tally a weapon's hits count toward, if it keeps one.</summary>
    public static Tally? HitsWith(string? abilityId) => abilityId switch
    {
        BroadsideVolley.AbilityId => Tally.BroadsideHits,
        LongGun.AbilityId => Tally.LongGunHits,
        Mortar.AbilityId => Tally.MortarHits,
        _ => null,
    };
}
