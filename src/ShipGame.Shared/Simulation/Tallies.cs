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
    /// <summary>
    /// <paramref name="count"/> of what it counts, in the HUD's capitals, singular or plural to suit: "12 BROADSIDE HITS",
    /// "1 KILL".
    /// </summary>
    public static string Count(Tally tally, float count)
    {
        var whole = MathF.Round(count);
        return $"{whole:0} {(whole == 1f ? Singular(tally) : Noun(tally))}";
    }

    /// <summary>One of what it counts: "BROADSIDE HIT", "SECOND AT ANCHOR".</summary>
    public static string Singular(Tally tally) => tally switch
    {
        Tally.BroadsideHits => "BROADSIDE HIT",
        Tally.LongGunHits => "LONG GUN HIT",
        Tally.MortarHits => "MORTAR HIT",
        Tally.Kills => "KILL",
        Tally.TilesSailed => "TILE SAILED",
        Tally.SecondsAnchored => "SECOND AT ANCHOR",
        _ => Noun(tally), // damage and gold don't take an S
    };

    /// <summary>What it counts, in the HUD's capitals, plural: "BROADSIDE HITS".</summary>
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
