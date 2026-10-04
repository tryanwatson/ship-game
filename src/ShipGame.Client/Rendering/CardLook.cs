using System;
using System.Linq;
using Microsoft.Xna.Framework;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Stats;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Client.Rendering;

/// <summary>
/// How cards look, wherever they're shown (the choice screen, the hand down the right): frames by tier, like Arena's
/// (silver, gold, a shifting prismatic), and each card's own color (its weapon's, or the ship's gold) and emblem.
/// </summary>
public static class CardLook
{
    private static readonly Color SilverFrame = new(195, 202, 214);
    private static readonly Color GoldFrame = new(240, 200, 90);
    private static readonly Color ShipFrame = new(240, 200, 90);
    private static readonly Color BroadsideFrame = new(245, 145, 70);
    private static readonly Color LongGunFrame = new(110, 200, 245);
    private static readonly Color MortarFrame = new(215, 125, 245);

    /// <summary>A prismatic's color right now: it runs slowly round the wheel, offset by <paramref name="phase"/>.</summary>
    public static Color Shimmer(float phase = 0f) => Hue(Environment.TickCount64 / 1000f * 0.15f + phase);

    public static Color TierColor(CardTier tier) => tier switch
    {
        CardTier.Silver => SilverFrame,
        CardTier.Gold => GoldFrame,
        _ => Hue(0f),
    };

    /// <summary>The card's own color: its weapon's, or the ship's gold.</summary>
    public static Color TagColor(CardDefinition card) => card.AbilityId is { } id ? WeaponFrame(id) : ShipFrame;

    /// <summary>A bright, fully saturated color round the wheel: 0 red, 1/3 green, 2/3 blue.</summary>
    public static Color Hue(float h)
    {
        h = (h % 1f + 1f) % 1f * 6f;
        var x = 1f - MathF.Abs(h % 2f - 1f);
        var (r, g, b) = h switch
        {
            < 1f => (1f, x, 0f),
            < 2f => (x, 1f, 0f),
            < 3f => (0f, 1f, x),
            < 4f => (0f, x, 1f),
            < 5f => (x, 0f, 1f),
            _ => (1f, 0f, x),
        };
        // Pastel, so it reads on the dark card without glaring.
        return new Color(0.55f + 0.45f * r, 0.55f + 0.45f * g, 0.55f + 0.45f * b);
    }

    public static Color WeaponFrame(string abilityId) => abilityId switch
    {
        BroadsideVolley.AbilityId => BroadsideFrame,
        LongGun.AbilityId => LongGunFrame,
        Mortar.AbilityId => MortarFrame,
        _ => ShipFrame,
    };

    public static string Category(CardDefinition card) =>
        card.AbilityId is { } id && WeaponCatalog.Find(id) is { } weapon ? weapon.Ability.Name.ToUpperInvariant() : "SHIP";

    /// <summary>The card's icon: its own for a few, otherwise from the first thing it changes.</summary>
    public static CardIcon Icon(CardDefinition card, float[] values)
    {
        switch (card.Id)
        {
            case "treasure-map" or "privateer":
                return CardIcon.Coin;
            case "free-armory" or "twin-decks":
                return CardIcon.Cannonballs;
            case "firestorm":
                return CardIcon.Fire;
            case "echo":
                return CardIcon.Echo;
        }
        if (card.WeaponEffects?.Invoke(values).FirstOrDefault() is { } effect && card.AbilityId is not null)
        {
            return effect.Stat switch
            {
                AbilityStat.Cooldown => CardIcon.Reload,
                AbilityStat.ShotCount or AbilityStat.CarpetShells when card.AbilityId == Mortar.AbilityId => CardIcon.Salvo,
                AbilityStat.ShotCount => CardIcon.Cannonballs,
                AbilityStat.Pierce or AbilityStat.Ricochets => CardIcon.Pierce,
                AbilityStat.BlastRadius => CardIcon.Blast,
                AbilityStat.ClusterCount => CardIcon.Cluster,
                AbilityStat.Range or AbilityStat.LongRangeDamage => CardIcon.Range,
                AbilityStat.FlightTime or AbilityStat.SlowOnHit => CardIcon.Reload,
                _ => CardIcon.Damage,
            };
        }
        if (card.Perks?.Invoke(values).FirstOrDefault() is { } perk && card.Stats is null)
        {
            return perk.Perk switch
            {
                Perk.RowingSpeed => CardIcon.Helm,
                Perk.SecondWindHeal => CardIcon.Shield,
                Perk.PrizeCrewHeal => CardIcon.Repair,
                Perk.RamDamage => CardIcon.Speed,
                Perk.HuntersMark => CardIcon.Range,
                _ => CardIcon.Damage,
            };
        }
        return card.Stats?.Invoke(values).FirstOrDefault() is not { } stat ? CardIcon.Damage : stat.Stat switch
        {
            StatId.MaxSpeed => CardIcon.Speed,
            StatId.MaxHealth => CardIcon.Shield,
            StatId.CooldownSpeed => CardIcon.Reload,
            StatId.WeaponRange => CardIcon.Range,
            StatId.HealthRegen or StatId.HealthRegenFraction => CardIcon.Repair,
            StatId.TurnRadius => CardIcon.Helm,
            _ => CardIcon.Damage,
        };
    }
}
