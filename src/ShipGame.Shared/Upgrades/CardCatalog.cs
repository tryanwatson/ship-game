using ShipGame.Shared.Abilities;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;
using static ShipGame.Shared.Abilities.AbilityStat;
using static ShipGame.Shared.Upgrades.SkillEffect;

namespace ShipGame.Shared.Upgrades;

/// <summary>How rare and how strong a card is. Harder fortresses deal the higher tiers (see <see cref="CardRewards"/>).</summary>
public enum CardTier
{
    /// <summary>Changes how you play, often at a cost.</summary>
    Silver,

    /// <summary>Big, clean boosts.</summary>
    Gold,

    /// <summary>Breaks a rule.</summary>
    Prismatic,
}

/// <summary>Ship-wide effects that aren't plain stats; cards grant them, and the simulation reads them where they apply.</summary>
public enum Perk
{
    /// <summary>Extra speed rowing with the sails furled: 1 rows twice as fast.</summary>
    RowingSpeed,

    /// <summary>Extra share of gold from kills and plunder: 0.5 is half as much again.</summary>
    GoldBonus,

    /// <summary>Second Wind: the share of max health a killing blow leaves instead.</summary>
    SecondWindHeal,

    /// <summary>Second Wind: seconds before it can save the ship again.</summary>
    SecondWindCooldown,

    /// <summary>Share of max health regained for each ship or fort sunk.</summary>
    PrizeCrewHeal,

    /// <summary>Damage dealt to whatever the ship runs into, at a stock sloop's top speed (see <see cref="World.RamDamageAt"/>).</summary>
    RamDamage,

    /// <summary>Extra damage, from everyone, to whatever the ship hits, for a few seconds.</summary>
    HuntersMark,

    /// <summary>Every weapon fires again a moment later, at this share of its damage.</summary>
    Echo,

    /// <summary>Every hit adds a stack of <see cref="StatusId.Burning"/>: damage a second per stack.</summary>
    BurnOnHit,

    /// <summary>Every kill adds a stack of <see cref="StatusId.Frenzy"/>: reload speed per stack (and half that in speed).</summary>
    Frenzy,

    /// <summary>Share of damage turned aside while anchored (Braced).</summary>
    Braced,

    /// <summary>At anchor, the helm swings the ship round on her cable, this many times the rowing rate (Spring Line).</summary>
    SpringLine,

    /// <summary>Every second with the anchor down adds a stack of <see cref="StatusId.Entrenched"/> this strong (Dug In).</summary>
    DugIn,

    /// <summary>At 1 or more, every weapon fires itself at the nearest enemy in reach while anchored (Floating Fortress).</summary>
    FortressGuns,

    /// <summary>Extra speed handling the anchor: 1 lets go and weighs twice as fast (Quick Anchor).</summary>
    AnchorHandling,
}

/// <summary>One change a card makes to the ship's own stats: always, or with <see cref="AtAnchor"/>, only while it's anchored.</summary>
public readonly record struct CardStat(StatId Stat, ModifierKind Kind, float Value, bool AtAnchor = false)
{
    public static CardStat Percent(StatId stat, float value) => new(stat, ModifierKind.Percent, value);

    public static CardStat Flat(StatId stat, float value) => new(stat, ModifierKind.Flat, value);

    /// <summary>The same change, made only while the ship is anchored.</summary>
    public CardStat Anchored => this with { AtAnchor = true };
}

/// <summary>
/// One of a card's numbers: <see cref="Low"/> at the lowest level its tier is dealt at, <see cref="High"/> at the
/// highest (see <see cref="CardDefinition.LevelRange"/>), in between by level. A rolled value is drawn at random, when
/// the card's dealt, from a range that runs from <see cref="Low"/>..<see cref="LowMax"/> up to
/// <see cref="High"/>..<see cref="HighMax"/>.
/// </summary>
public readonly record struct CardValue(float Low, float High, float? LowMax = null, float? HighMax = null)
{
    public bool IsRolled => LowMax is not null;

    public float At(float t, float roll)
    {
        var min = Low + (High - Low) * t;
        if (LowMax is not { } lowMax)
            return min;
        var max = lowMax + ((HighMax ?? lowMax) - lowMax) * t;
        return min + (max - min) * roll;
    }
}

/// <summary>
/// A card that grows: every <see cref="Every"/> of a ship's <see cref="Tally"/>, it applies once more. It starts at
/// nothing, so its numbers are what each step adds.
/// </summary>
public sealed record CardGrowth(Tally Tally, float Every);

/// <summary>A card in a hand or on offer: which card, the level its numbers were dealt at, and its roll (0..1) for rolled values.</summary>
public readonly record struct CardPick(string Id, int Level, float Roll = 0f)
{
    public CardDefinition Definition => CardCatalog.Get(Id);

    public float[] Values => Definition.ValuesFor(this);

    public string Description => Definition.Describe(Values);

    /// <summary>How it works out on <paramref name="ship"/> right now, for cards whose numbers depend on the ship; else null.</summary>
    public string? DescriptionOn(Ship ship) => Definition.DescribeOn?.Invoke(Values, ship);
}

/// <summary>
/// A card: a permanent change to the ship (or, for a few, something that happens the moment it's chosen). Its numbers
/// scale with the level it's dealt at (see <see cref="CardValue"/>), and what it does is computed from them: ship stats,
/// weapon changes (read by <see cref="AbilityId"/>'s ability, like a skill's), and perks. Silver and gold cards stack
/// when taken again; a prismatic taken again improves instead (see <see cref="CardStacking"/>).
/// </summary>
public sealed class CardDefinition
{
    public CardDefinition(string id, string name, CardTier tier)
    {
        Id = id;
        Name = name;
        Tier = tier;
    }

    public string Id { get; }

    public string Name { get; }

    public CardTier Tier { get; }

    /// <summary>The weapon a weapon card improves: it's only offered to ships carrying it. Null for cards that suit any ship.</summary>
    public string? AbilityId { get; init; }

    public CardValue[] Values { get; init; } = Array.Empty<CardValue>();

    /// <summary>What it does, in the HUD's capitals, given its numbers.</summary>
    public required Func<float[], string> Describe { get; init; }

    /// <summary>For a card whose effect depends on the ship (its speed, say): what that comes to on a given ship, right now.</summary>
    public Func<float[], Ship, string>? DescribeOn { get; init; }

    public Func<float[], IEnumerable<CardStat>>? Stats { get; init; }

    /// <summary>For a card that grows: what with, and how often. Its stats, weapon changes and perks apply once a step.</summary>
    public CardGrowth? Growth { get; init; }

    public Func<float[], IEnumerable<SkillEffect>>? WeaponEffects { get; init; }

    public Func<float[], IEnumerable<(Perk Perk, float Value)>>? Perks { get; init; }

    /// <summary>Something that happens once, the moment it's chosen (gold, a free weapon). Such a card does nothing after.</summary>
    public Action<World, PlayerState, float[], Random>? OnChosen { get; init; }

    public bool OneShot => OnChosen is not null;

    /// <summary>Taken again, it improves rather than stacking: prismatics, other than one-shots.</summary>
    public bool Improves => Tier == CardTier.Prismatic && !OneShot;

    public bool IsRolled => Values.Any(v => v.IsRolled);

    /// <summary>Tags what it grants, like an upgrade's source.</summary>
    public string Source => "card:" + Id;

    /// <summary>The levels its tier is dealt at: its numbers run from low at the first to high at the last.</summary>
    public (int Min, int Max) LevelRange => Tier switch
    {
        CardTier.Silver => (1, 6),
        CardTier.Gold => (1, 7),
        _ => (3, 8),
    };

    /// <summary>Levels a prismatic can be improved past its tier's last, by taking it again.</summary>
    public const int MaxImprovement = 3;

    public float[] ValuesFor(CardPick pick)
    {
        var (min, max) = LevelRange;
        var t = Math.Clamp((pick.Level - min) / (float)(max - min), 0f, 1f + MaxImprovement / (float)(max - min));
        return Values.Select(v => v.At(t, pick.Roll)).ToArray();
    }

    /// <param name="steps">Times it applies: 1, or for a growing card, how far it's grown (see <see cref="Growth"/>).</param>
    /// <param name="atAnchor">Which: the changes it always makes, or those it makes only while anchored.</param>
    public IEnumerable<StatModifier> StatModifiersFor(CardPick pick, int steps = 1, bool atAnchor = false) =>
        Stats is null || steps <= 0 ? Enumerable.Empty<StatModifier>()
            : Stats(ValuesFor(pick)).Where(s => s.AtAnchor == atAnchor)
                .Select(s => new StatModifier(s.Stat, s.Kind, Repeated(s.Kind, s.Value, steps), Source));

    public IEnumerable<AbilityModifier> AbilityModifiersFor(CardPick pick, int steps = 1) =>
        AbilityId is { } abilityId && WeaponEffects is not null && steps > 0
            ? WeaponEffects(ValuesFor(pick)).Select(e => new AbilityModifier(abilityId, e.Stat, e.Kind, Repeated(e.Kind, e.Value, steps), Source))
            : Enumerable.Empty<AbilityModifier>();

    public IEnumerable<(Perk Perk, float Value)> PerksFor(CardPick pick, int steps = 1) =>
        Perks is null || steps <= 0 ? Enumerable.Empty<(Perk, float)>() : Perks(ValuesFor(pick)).Select(p => (p.Perk, p.Value * steps));

    /// <summary>A change applied <paramref name="steps"/> times over: added that many times, or for a multiplier, multiplied.</summary>
    private static float Repeated(ModifierKind kind, float value, int steps) =>
        steps == 1 ? value : kind == ModifierKind.Multiplier ? MathF.Pow(value, steps) : value * steps;
}

/// <summary>Adding a card to a hand (a player's, a ship's): silver and gold stack, a prismatic taken again improves.</summary>
public static class CardStacking
{
    /// <summary>The pick a hand would hold for <paramref name="pick"/>: an improved copy of a prismatic already held.</summary>
    public static CardPick WouldBecome(IEnumerable<CardPick> hand, CardPick pick)
    {
        if (!pick.Definition.Improves)
            return pick;
        var held = hand.Where(p => p.Id == pick.Id).ToList();
        return held.Count == 0 ? pick : pick with { Level = Math.Max(pick.Level, held.Max(p => p.Level)) + 1 };
    }

    public static void Add(List<CardPick> hand, CardPick pick)
    {
        var becomes = WouldBecome(hand, pick);
        if (pick.Definition.Improves)
            hand.RemoveAll(p => p.Id == pick.Id);
        hand.Add(becomes);
    }
}

/// <summary>
/// Every card. First-pass numbers, meant to feel big rather than to balance. A value is written low → high over its
/// tier's levels (silver 1-6, gold 1-7, prismatic 3-8). Add a card by adding a row: ship stats go through
/// <see cref="StatId"/>, weapon changes through <see cref="AbilityStat"/>, other effects through <see cref="Perk"/>.
/// </summary>
public static class CardCatalog
{
    private static string P(float v) => $"{MathF.Round(v * 100f):0}%";
    private static string P1(float v) => $"{MathF.Round(v * 1000f) / 10f:0.#}%";
    private static string N(float v) => $"{MathF.Round(v):0}";
    private static string D(float v) => $"{MathF.Round(v * 10f) / 10f:0.#}";
    private static int Whole(float v) => (int)MathF.Round(v);

    /// <summary>"Reloads x faster", as a multiplier on reload time: 50% faster is two thirds the time.</summary>
    private static float Faster(float x) => 1f / (1f + x);

    private static CardValue V(float low, float high) => new(low, high);

    private static CardValue Roll(float lowMin, float lowMax, float highMin, float highMax) => new(lowMin, highMin, lowMax, highMax);

    /// <summary>
    /// A card that grows (see <see cref="CardGrowth"/>): every <paramref name="every"/> of <paramref name="tally"/>,
    /// <paramref name="what"/> once more, by its one value. It says so ("EVERY 50 BROADSIDE HITS: +3% BROADSIDE
    /// DAMAGE."), and on a ship, how far it's grown and how far to the next step.
    /// </summary>
    private static CardDefinition Growing(string id, string name, Tally tally, float every, CardValue value, Func<float, string> what,
        string? abilityId = null, Func<float, CardStat>? stat = null, Func<float, SkillEffect>? weapon = null)
    {
        var growth = new CardGrowth(tally, every);
        return new CardDefinition(id, name, CardTier.Gold)
        {
            AbilityId = abilityId,
            Growth = growth,
            Values = new[] { value },
            Describe = v => $"EVERY {N(every)} {Tallies.Noun(tally)}: {what(v[0])}.",
            DescribeOn = (v, ship) =>
            {
                var count = ship.TallyOf(tally);
                var steps = (int)MathF.Floor(count / every);
                var next = MathF.Max(1f, MathF.Ceiling((steps + 1) * every - count));
                return $"NOW {(steps == 0 ? "NOTHING YET" : what(v[0] * steps))}. NEXT IN {N(next)} {Tallies.Noun(tally)}.";
            },
            Stats = stat is null ? null : v => new[] { stat(v[0]) },
            WeaponEffects = weapon is null ? null : v => new[] { weapon(v[0]) },
        };
    }

    public static readonly IReadOnlyList<CardDefinition> All = new CardDefinition[]
    {
        // ---- Silver: changes how you play ------------------------------------------------------------------
        new("glass-cannon", "GLASS CANNON", CardTier.Silver)
        {
            Values = new[] { V(0.4f, 0.8f) },
            Describe = v => $"+{P(v[0])} DAMAGE WITH EVERY WEAPON. -30% MAX HEALTH.",
            Stats = v => new[] { CardStat.Percent(StatId.WeaponDamage, v[0]), CardStat.Percent(StatId.MaxHealth, -0.3f) },
        },
        new("juggernaut", "JUGGERNAUT", CardTier.Silver)
        {
            Values = new[] { V(0.6f, 1.2f) },
            Describe = v => $"+{P(v[0])} MAX HEALTH. -20% SPEED.",
            Stats = v => new[] { CardStat.Percent(StatId.MaxHealth, v[0]), CardStat.Percent(StatId.MaxSpeed, -0.2f) },
        },
        new("clipper", "CLIPPER", CardTier.Silver)
        {
            Values = new[] { V(0.25f, 0.5f) },
            Describe = v => $"+{P(v[0])} SPEED. -20% MAX HEALTH.",
            Stats = v => new[] { CardStat.Percent(StatId.MaxSpeed, v[0]), CardStat.Percent(StatId.MaxHealth, -0.2f) },
        },
        new("sea-legs", "SEA LEGS", CardTier.Silver)
        {
            Values = new[] { V(0.3f, 0.6f) },
            Describe = v => $"TURN {P(v[0])} TIGHTER.",
            // Compounds rather than adds, so copies can't drive the radius through zero (see StatModifiers.TightestTurn).
            Stats = v => new[] { new CardStat(StatId.TurnRadius, ModifierKind.Multiplier, 1f - v[0]) },
        },
        new("patchwork-hull", "PATCHWORK HULL", CardTier.Silver)
        {
            Values = new[] { V(1f, 3f) },
            Describe = v => $"REGENERATE +{D(v[0])} HEALTH EVERY SECOND.",
            Stats = v => new[] { CardStat.Flat(StatId.HealthRegen, v[0]) },
        },
        new("long-gunnery", "LONG GUNNERY", CardTier.Silver)
        {
            Values = new[] { V(0.2f, 0.4f) },
            Describe = v => $"+{P(v[0])} RANGE WITH EVERY WEAPON. -15% DAMAGE.",
            Stats = v => new[] { CardStat.Percent(StatId.WeaponRange, v[0]), CardStat.Percent(StatId.WeaponDamage, -0.15f) },
        },
        new("rowers", "ROWERS", CardTier.Silver)
        {
            Values = new[] { V(2f, 4f) },
            Describe = v => $"WITH THE SAILS FURLED, ROW ASTERN AND SWING ROUND {D(v[0])}X FASTER.",
            Perks = v => new[] { (Perk.RowingSpeed, v[0] - 1f) },
        },
        new("privateer", "PRIVATEER", CardTier.Silver)
        {
            Values = new[] { V(0.25f, 0.75f) },
            Describe = v => $"+{P(v[0])} GOLD FROM KILLS AND PLUNDER.",
            Perks = v => new[] { (Perk.GoldBonus, v[0]) },
        },
        new("spring-line", "SPRING LINE", CardTier.Silver)
        {
            Values = new[] { V(1.5f, 3f) },
            Describe = v => $"AT ANCHOR, THE HELM SWINGS THE SHIP ROUND ON HER CABLE, {D(v[0])}X AS FAST AS ROWING.",
            Perks = v => new[] { (Perk.SpringLine, v[0]) },
        },
        new("quick-anchor", "QUICK ANCHOR", CardTier.Silver)
        {
            Values = new[] { V(1f, 3f) },
            Describe = v => $"LET GO AND WEIGH THE ANCHOR {D(1f + v[0])}X AS FAST.",
            Perks = v => new[] { (Perk.AnchorHandling, v[0]) },
        },
        new("close-quarters", "CLOSE QUARTERS", CardTier.Silver)
        {
            AbilityId = BroadsideVolley.AbilityId,
            Values = new[] { V(0.6f, 1.2f) },
            Describe = v => $"+{P(v[0])} BROADSIDE DAMAGE WITHIN HALF RANGE. -25% RANGE.",
            WeaponEffects = v => new[] { Flat(CloseRangeDamage, v[0]), Times(AbilityStat.Range, 0.75f) },
        },
        new("running-guns", "RUNNING GUNS", CardTier.Silver)
        {
            AbilityId = BroadsideVolley.AbilityId,
            Values = new[] { V(0.5f, 1f) },
            Describe = v => $"+{P(v[0])} BROADSIDE DAMAGE WHILE NEAR FULL SAIL.",
            WeaponEffects = v => new[] { Flat(SpeedDamage, v[0]) },
        },
        new("light-battery", "LIGHT BATTERY", CardTier.Silver)
        {
            AbilityId = BroadsideVolley.AbilityId,
            Values = new[] { V(0.3f, 0.5f) },
            Describe = v => $"BROADSIDES RELOAD {P(v[0])} FASTER. ONE FEWER CANNON.",
            WeaponEffects = v => new[] { Times(Cooldown, Faster(v[0])), Flat(ShotCount, -1f) },
        },
        new("marksman", "MARKSMAN", CardTier.Silver)
        {
            AbilityId = LongGun.AbilityId,
            Values = new[] { V(0.5f, 1f) },
            Describe = v => $"+{P(v[0])} LONG GUN DAMAGE ON HITS BEYOND 60% OF ITS REACH.",
            WeaponEffects = v => new[] { Flat(LongRangeDamage, v[0]) },
        },
        new("snap-shot", "SNAP SHOT", CardTier.Silver)
        {
            AbilityId = LongGun.AbilityId,
            Values = new[] { V(0.25f, 0.45f) },
            Describe = v => $"THE LONG GUN RELOADS {P(v[0])} FASTER. -20% DAMAGE.",
            WeaponEffects = v => new[] { Times(Cooldown, Faster(v[0])), Times(Damage, 0.8f) },
        },
        new("siege-gunner", "SIEGE GUNNER", CardTier.Silver)
        {
            AbilityId = Mortar.AbilityId,
            Values = new[] { V(0.5f, 1f) },
            Describe = v => $"+{P(v[0])} MORTAR RANGE. IT RELOADS 30% SLOWER.",
            WeaponEffects = v => new[] { Times(AbilityStat.Range, 1f + v[0]), Times(Cooldown, 1.3f) },
        },
        new("quick-fuse", "QUICK FUSE", CardTier.Silver)
        {
            AbilityId = Mortar.AbilityId,
            Values = new[] { V(0.3f, 0.6f) },
            Describe = v => $"MORTAR SHELLS LAND {P(v[0])} SOONER.",
            WeaponEffects = v => new[] { Times(FlightTime, 1f - v[0]) },
        },

        // ---- Gold: big, clean boosts -----------------------------------------------------------------------
        new("heated-shot", "HEATED SHOT", CardTier.Gold)
        {
            Values = new[] { V(3f, 8f) },
            Describe = v => $"EVERY HIT SETS ITS TARGET BURNING: {N(v[0])} DAMAGE A SECOND FOR EACH STACK, UP TO "
                            + $"{Statuses.Get(StatusId.Burning).MaxStacks}. EACH HIT KEEPS IT BURNING {N(Statuses.Get(StatusId.Burning).Seconds)} SECONDS MORE.",
            Perks = v => new[] { (Perk.BurnOnHit, v[0]) },
        },
        new("frenzy", "FRENZY", CardTier.Gold)
        {
            Values = new[] { V(0.06f, 0.12f) },
            Describe = v => $"EVERY SHIP YOU SINK ADDS A STACK OF FRENZY, UP TO {Statuses.Get(StatusId.Frenzy).MaxStacks}: +{P(v[0])} RELOAD SPEED "
                            + $"AND +{P(v[0] / 2f)} SPEED EACH. IT LASTS {N(Statuses.Get(StatusId.Frenzy).Seconds)} SECONDS FROM YOUR LAST KILL.",
            Perks = v => new[] { (Perk.Frenzy, v[0]) },
        },
        new("full-sail", "FULL SAIL", CardTier.Gold)
        {
            Values = new[] { V(0.3f, 0.7f) },
            Describe = v => $"+{P(v[0])} SPEED.",
            Stats = v => new[] { CardStat.Percent(StatId.MaxSpeed, v[0]) },
        },
        new("ironclad", "IRONCLAD", CardTier.Gold)
        {
            Values = new[] { V(0.6f, 1.5f) },
            Describe = v => $"+{P(v[0])} MAX HEALTH.",
            Stats = v => new[] { CardStat.Percent(StatId.MaxHealth, v[0]) },
        },
        new("quick-hands", "QUICK HANDS", CardTier.Gold)
        {
            Values = new[] { V(0.3f, 0.7f) },
            Describe = v => $"EVERY WEAPON RELOADS {P(v[0])} FASTER.",
            Stats = v => new[] { CardStat.Percent(StatId.CooldownSpeed, v[0]) },
        },
        new("heavy-shot", "HEAVY SHOT", CardTier.Gold)
        {
            Values = new[] { V(0.3f, 0.75f) },
            Describe = v => $"+{P(v[0])} DAMAGE WITH EVERY WEAPON.",
            Stats = v => new[] { CardStat.Percent(StatId.WeaponDamage, v[0]) },
        },
        new("eagle-eye", "EAGLE EYE", CardTier.Gold)
        {
            Values = new[] { V(0.25f, 0.6f) },
            Describe = v => $"+{P(v[0])} RANGE WITH EVERY WEAPON.",
            Stats = v => new[] { CardStat.Percent(StatId.WeaponRange, v[0]) },
        },
        new("shipwrights", "SHIPWRIGHTS", CardTier.Gold)
        {
            Values = new[] { V(0.01f, 0.05f) },
            Describe = v => $"REGENERATE {P1(v[0])} OF MAX HEALTH EVERY SECOND.",
            Stats = v => new[] { CardStat.Flat(StatId.HealthRegenFraction, v[0]) },
        },
        new("battery-station", "BATTERY STATION", CardTier.Gold)
        {
            Values = new[] { V(0.3f, 0.6f), V(0.15f, 0.3f) },
            Describe = v => $"AT ANCHOR, EVERY WEAPON RELOADS {P(v[0])} FASTER AND REACHES {P(v[1])} FURTHER.",
            Stats = v => new[]
            {
                CardStat.Percent(StatId.CooldownSpeed, v[0]).Anchored, CardStat.Percent(StatId.WeaponRange, v[1]).Anchored,
            },
        },
        new("dug-in", "DUG IN", CardTier.Gold)
        {
            Values = new[] { V(0.04f, 0.08f) },
            Describe = v => $"EVERY SECOND WITH THE ANCHOR DOWN ADDS A STACK OF ENTRENCHED, UP TO {Statuses.Get(StatusId.Entrenched).MaxStacks}: "
                            + $"+{P(v[0])} DAMAGE WITH EVERY WEAPON EACH. IT FADES {N(Statuses.Get(StatusId.Entrenched).Seconds)} SECONDS AFTER YOU START WEIGHING.",
            Perks = v => new[] { (Perk.DugIn, v[0]) },
        },
        new("braced", "BRACED", CardTier.Gold)
        {
            Values = new[] { V(0.2f, 0.4f) },
            Describe = v => $"AT ANCHOR, TAKE {P(v[0])} LESS DAMAGE.",
            Perks = v => new[] { (Perk.Braced, v[0]) },
        },
        new("treasure-map", "TREASURE MAP", CardTier.Gold)
        {
            Values = new[] { Roll(100f, 200f, 400f, 800f) },
            Describe = v => $"{N(v[0])} GOLD, RIGHT AWAY.",
            OnChosen = (world, player, v, _) => world.AddGold(player.PlayerId, Whole(v[0])),
        },
        new("double-battery", "DOUBLE BATTERY", CardTier.Gold)
        {
            AbilityId = BroadsideVolley.AbilityId,
            Values = new[] { V(2f, 6f) },
            Describe = v => $"+{N(v[0])} CANNON IN EVERY BROADSIDE.",
            WeaponEffects = v => new[] { Flat(ShotCount, Whole(v[0])) },
        },
        new("rapid-broadsides", "RAPID BROADSIDES", CardTier.Gold)
        {
            AbilityId = BroadsideVolley.AbilityId,
            Values = new[] { V(0.3f, 0.65f) },
            Describe = v => $"BROADSIDES RELOAD {P(v[0])} FASTER.",
            WeaponEffects = v => new[] { Times(Cooldown, Faster(v[0])) },
        },
        new("swivel-guns", "SWIVEL GUNS", CardTier.Gold)
        {
            AbilityId = BroadsideVolley.AbilityId,
            Values = new[] { V(15f, 30f) },
            Describe = v => $"BROADSIDES AIM {N(v[0])} DEGREES FURTHER FORE AND AFT.",
            WeaponEffects = v => new[] { Flat(AimArc, Whole(v[0])) },
        },
        new("powder-kegs", "POWDER KEGS", CardTier.Gold)
        {
            AbilityId = BroadsideVolley.AbilityId,
            Values = new[] { V(0.4f, 1f) },
            Describe = v => $"+{P(v[0])} BROADSIDE DAMAGE.",
            WeaponEffects = v => new[] { Times(Damage, 1f + v[0]) },
        },
        new("gun-captains", "GUN CAPTAINS", CardTier.Gold)
        {
            AbilityId = BroadsideVolley.AbilityId,
            Values = new[] { V(0.15f, 0.4f) },
            Describe = v => $"EACH BROADSIDE DECK FIRES BY ITSELF WHENEVER IT'S LOADED AND AN ENEMY IS IN ITS LANE. RELOADS {P(v[0])} FASTER.",
            WeaponEffects = v => new[] { Flat(AutoFire, 1f), Times(Cooldown, Faster(v[0])) },
        },
        new("grapeshot", "GRAPESHOT", CardTier.Gold)
        {
            AbilityId = BroadsideVolley.AbilityId,
            Values = new[] { V(2f, 4f) },
            Describe = v => $"EVERY CANNON FIRES {N(v[0])} MORE BALLS OF GRAPE, FANNED OUT. A BALL OF GRAPE DOES {P(BroadsideVolley.GrapeDamageFraction)} DAMAGE.",
            WeaponEffects = v => new[] { Flat(Grapeshot, Whole(v[0])) },
        },
        new("skip-shot", "SKIP SHOT", CardTier.Gold)
        {
            AbilityId = BroadsideVolley.AbilityId,
            Values = new[] { V(1f, 3f) },
            Describe = v => Whole(v[0]) == 1 ? "CANNONBALLS SKIP ON ONCE, OFF THE WATER OR WHATEVER THEY HIT."
                : $"CANNONBALLS SKIP ON UP TO {N(v[0])} TIMES, OFF THE WATER OR WHATEVER THEY HIT.",
            WeaponEffects = v => new[] { Flat(Skips, Whole(v[0])) },
        },
        new("hot-guns", "HOT GUNS", CardTier.Gold)
        {
            AbilityId = BroadsideVolley.AbilityId,
            Values = new[] { V(0.04f, 0.1f) },
            Describe = v => $"EVERY BROADSIDE HIT TAKES {P1(v[0])} OF THE RELOAD OFF BOTH DECKS.",
            WeaponEffects = v => new[] { Flat(HitRefund, v[0]) },
        },
        new("incendiary-shot", "INCENDIARY SHOT", CardTier.Gold)
        {
            AbilityId = BroadsideVolley.AbilityId,
            Values = new[] { V(2f, 4f), V(6f, 15f) },
            Describe = v => $"BROADSIDE HITS SET THE WATER BURNING FOR {D(v[0])} SECONDS, {N(v[1])} DAMAGE A SECOND.",
            WeaponEffects = v => new[] { Flat(FireSeconds, v[0]), Flat(FireDps, v[1]) },
        },
        new("rifled-barrel", "RIFLED BARREL", CardTier.Gold)
        {
            AbilityId = LongGun.AbilityId,
            Values = new[] { V(0.3f, 0.65f) },
            Describe = v => $"THE LONG GUN RELOADS {P(v[0])} FASTER.",
            WeaponEffects = v => new[] { Times(Cooldown, Faster(v[0])) },
        },
        new("piercing-rounds", "PIERCING ROUNDS", CardTier.Gold)
        {
            AbilityId = LongGun.AbilityId,
            Values = new[] { V(1f, 4f) },
            Describe = v => Whole(v[0]) == 1 ? "LONG GUN SHOTS PASS THROUGH A SHIP. +25% DAMAGE."
                : $"LONG GUN SHOTS PASS THROUGH {N(v[0])} SHIPS. +25% DAMAGE.",
            WeaponEffects = v => new[] { Flat(Pierce, Whole(v[0])), Times(Damage, 1.25f) },
        },
        new("big-bore", "BIG BORE", CardTier.Gold)
        {
            AbilityId = LongGun.AbilityId,
            Values = new[] { V(0.5f, 1.25f) },
            Describe = v => $"+{P(v[0])} LONG GUN DAMAGE.",
            WeaponEffects = v => new[] { Times(Damage, 1f + v[0]) },
        },
        new("explosive-rounds", "EXPLOSIVE ROUNDS", CardTier.Gold)
        {
            AbilityId = LongGun.AbilityId,
            Values = new[] { V(2f, 3.5f), V(0.5f, 1f) },
            Describe = v => $"LONG GUN HITS EXPLODE, HURTING EVERYTHING WITHIN {D(v[0])} TILES FOR {P(v[1])} OF THE SHOT'S DAMAGE.",
            WeaponEffects = v => new[] { Flat(ExplosionRadius, v[0]), Flat(ExplosionDamage, v[1]) },
        },
        new("burning-wake", "BURNING WAKE", CardTier.Gold)
        {
            AbilityId = LongGun.AbilityId,
            Values = new[] { V(2f, 4f), V(8f, 20f) },
            Describe = v => $"LONG GUN SHOTS LEAVE THE WATER BURNING BEHIND THEM FOR {D(v[0])} SECONDS, {N(v[1])} DAMAGE A SECOND.",
            WeaponEffects = v => new[] { Flat(FireSeconds, v[0]), Flat(FireDps, v[1]) },
        },
        new("volley-gun", "VOLLEY GUN", CardTier.Gold)
        {
            AbilityId = LongGun.AbilityId,
            Values = new[] { V(2f, 4f) },
            Describe = v => $"THE LONG GUN FIRES {N(v[0])} MORE SHOTS AT ONCE, IN A FAN.",
            WeaponEffects = v => new[] { Flat(ShotCount, Whole(v[0])) },
        },
        new("mortar-crew", "MORTAR CREW", CardTier.Gold)
        {
            AbilityId = Mortar.AbilityId,
            Values = new[] { V(0.3f, 0.65f) },
            Describe = v => $"THE MORTAR RELOADS {P(v[0])} FASTER.",
            WeaponEffects = v => new[] { Times(Cooldown, Faster(v[0])) },
        },
        new("triple-salvo", "TRIPLE SALVO", CardTier.Gold)
        {
            AbilityId = Mortar.AbilityId,
            Values = new[] { V(1f, 4f) },
            Describe = v => Whole(v[0]) == 1 ? "THE MORTAR FIRES 1 MORE SHELL EACH TIME." : $"THE MORTAR FIRES {N(v[0])} MORE SHELLS EACH TIME.",
            WeaponEffects = v => new[] { Flat(ShotCount, Whole(v[0])) },
        },
        new("big-shells", "BIG SHELLS", CardTier.Gold)
        {
            AbilityId = Mortar.AbilityId,
            Values = new[] { V(0.3f, 0.8f), V(0.2f, 0.5f) },
            Describe = v => $"+{P(v[0])} BLAST RADIUS. +{P(v[1])} MORTAR DAMAGE.",
            WeaponEffects = v => new[] { Times(BlastRadius, 1f + v[0]), Times(Damage, 1f + v[1]) },
        },
        new("cluster-bombs", "CLUSTER BOMBS", CardTier.Gold)
        {
            AbilityId = Mortar.AbilityId,
            Values = new[] { V(3f, 10f) },
            Describe = v => $"EVERY SHELL SCATTERS {N(v[0])} EXTRA EXPLOSIONS.",
            WeaponEffects = v => new[] { Flat(ClusterCount, Whole(v[0])) },
        },

        // ---- Gold that grows: every so many of something, a little more --------------------------------------
        Growing("gunnery-drill", "GUNNERY DRILL", Tally.BroadsideHits, 50f, V(0.02f, 0.05f), x => $"+{P1(x)} BROADSIDE DAMAGE",
            abilityId: BroadsideVolley.AbilityId, weapon: x => Percent(Damage, x)),
        Growing("sharpshooter", "SHARPSHOOTER", Tally.LongGunHits, 10f, V(0.03f, 0.06f), x => $"+{P1(x)} LONG GUN DAMAGE",
            abilityId: LongGun.AbilityId, weapon: x => Percent(Damage, x)),
        Growing("bombardier", "BOMBARDIER", Tally.MortarHits, 40f, V(0.02f, 0.05f), x => $"+{P1(x)} MORTAR DAMAGE",
            abilityId: Mortar.AbilityId, weapon: x => Percent(Damage, x)),
        Growing("sea-miles", "SEA MILES", Tally.TilesSailed, 250f, V(0.01f, 0.025f), x => $"+{P1(x)} SPEED",
            stat: x => CardStat.Percent(StatId.MaxSpeed, x)),
        Growing("battle-hardened", "BATTLE-HARDENED", Tally.DamageTaken, 250f, V(0.02f, 0.05f), x => $"+{P1(x)} MAX HEALTH",
            stat: x => CardStat.Percent(StatId.MaxHealth, x)),
        Growing("bounty-hunter", "BOUNTY HUNTER", Tally.Kills, 5f, V(0.02f, 0.05f), x => $"+{P1(x)} DAMAGE WITH EVERY WEAPON",
            stat: x => CardStat.Percent(StatId.WeaponDamage, x)),
        Growing("war-chest", "WAR CHEST", Tally.GoldEarned, 100f, V(0.01f, 0.03f), x => $"+{P1(x)} RELOAD SPEED",
            stat: x => CardStat.Percent(StatId.CooldownSpeed, x)),
        Growing("siege-engineer", "SIEGE ENGINEER", Tally.SecondsAnchored, 30f, V(0.02f, 0.04f), x => $"+{P1(x)} RANGE WITH EVERY WEAPON",
            stat: x => CardStat.Percent(StatId.WeaponRange, x)),
        Growing("sawbones", "SAWBONES", Tally.DamageDealt, 500f, V(0.3f, 0.8f), x => $"+{D(x)} HEALTH A SECOND",
            stat: x => CardStat.Flat(StatId.HealthRegen, x)),

        // ---- Prismatic: breaks a rule ----------------------------------------------------------------------
        new("second-wind", "SECOND WIND", CardTier.Prismatic)
        {
            Values = new[] { V(0.3f, 0.6f), V(90f, 45f) },
            Describe = v => $"SURVIVE A KILLING BLOW WITH {P(v[0])} HEALTH. ONCE EVERY {N(v[1])} SECONDS.",
            Perks = v => new[] { (Perk.SecondWindHeal, v[0]), (Perk.SecondWindCooldown, MathF.Max(10f, v[1])) },
        },
        new("prize-crew", "PRIZE CREW", CardTier.Prismatic)
        {
            Values = new[] { V(0.15f, 0.35f) },
            Describe = v => $"SINKING A SHIP OR FORT HEALS YOU {P(v[0])} OF MAX HEALTH.",
            Perks = v => new[] { (Perk.PrizeCrewHeal, v[0]) },
        },
        new("ram", "RAM", CardTier.Prismatic)
        {
            Values = new[] { V(40f, 100f) },
            Describe = v => $"RAMMING A SHIP DEALS {N(v[0])} DAMAGE AT A SLOOP'S TOP SPEED, MORE IF FASTER. YOU TAKE NONE.",
            DescribeOn = (v, ship) => $"AT YOUR TOP SPEED: {N(World.RamDamageAt(v[0], ship.Stats.MaxSpeed))} DAMAGE.",
            Perks = v => new[] { (Perk.RamDamage, v[0]) },
        },
        new("hunters-mark", "HUNTERS MARK", CardTier.Prismatic)
        {
            Values = new[] { V(0.25f, 0.6f) },
            Describe = v => $"ANYTHING YOU HIT TAKES +{P(v[0])} DAMAGE FROM EVERYONE FOR 5 SECONDS.",
            Perks = v => new[] { (Perk.HuntersMark, v[0]) },
        },
        new("echo", "ECHO", CardTier.Prismatic)
        {
            Values = new[] { V(0.4f, 0.8f) },
            Describe = v => $"EVERY WEAPON FIRES AGAIN HALF A SECOND LATER, AT {P(v[0])} DAMAGE.",
            Perks = v => new[] { (Perk.Echo, v[0]) },
        },
        new("floating-fortress", "FLOATING FORTRESS", CardTier.Prismatic)
        {
            Values = new[] { V(0.2f, 0.5f) },
            Describe = v => $"AT ANCHOR, EVERY WEAPON FIRES BY ITSELF AT THE NEAREST ENEMY IN REACH, LEADING ITS AIM. +{P(v[0])} RANGE AT ANCHOR.",
            Stats = v => new[] { CardStat.Percent(StatId.WeaponRange, v[0]).Anchored },
            Perks = v => new[] { (Perk.FortressGuns, 1f) },
        },
        new("free-armory", "FREE ARMORY", CardTier.Prismatic)
        {
            Describe = _ => "A RANDOM WEAPON YOU DON'T HAVE, FREE. HAVE THEM ALL? A RANDOM SKILL INSTEAD.",
            OnChosen = (world, player, _, rng) => Armory.GiveSomething(world, player, rng),
        },
        new("twin-decks", "TWIN DECKS", CardTier.Prismatic)
        {
            AbilityId = BroadsideVolley.AbilityId,
            Values = new[] { V(0f, 0.4f) },
            Describe = v => v[0] < 0.005f ? "EVERY BROADSIDE FIRES FROM BOTH SIDES AT ONCE."
                : $"EVERY BROADSIDE FIRES FROM BOTH SIDES AT ONCE. +{P(v[0])} DAMAGE.",
            WeaponEffects = v => new[] { Flat(BothSides, 1f), Times(Damage, 1f + v[0]) },
        },
        new("chain-shot", "CHAIN SHOT", CardTier.Prismatic)
        {
            AbilityId = BroadsideVolley.AbilityId,
            Values = new[] { V(0.3f, 0.6f) },
            Describe = v => $"BROADSIDE HITS SLOW THE SHIP THEY STRIKE BY {P(v[0])} FOR 3 SECONDS.",
            WeaponEffects = v => new[] { Flat(SlowOnHit, MathF.Min(0.85f, v[0])) },
        },
        new("man-o-war", "MAN O' WAR", CardTier.Prismatic)
        {
            AbilityId = BroadsideVolley.AbilityId,
            Values = new[] { V(0f, 0.5f) },
            Describe = v => "THE BROADSIDE FIRES A RING OF SHOT ALL ROUND THE SHIP, BY ITSELF, WHENEVER AN ENEMY IS IN RANGE."
                            + (v[0] < 0.005f ? "" : $" +{P(v[0])} DAMAGE."),
            DescribeOn = (_, ship) =>
                $"YOUR RING: {BroadsideVolley.RingShotsFor(ship)} BALLS, EVERY {BroadsideVolley.ReloadSecondsFor(ship):0.0} SECONDS.",
            WeaponEffects = v => new[] { Flat(Ring, 1f), Times(Damage, 1f + v[0]) },
        },
        new("railgun", "RAILGUN", CardTier.Prismatic)
        {
            AbilityId = LongGun.AbilityId,
            Values = new[] { V(0.5f, 1.5f) },
            Describe = v => $"DOUBLE LONG GUN RANGE. SHOTS GO THROUGH EVERY SHIP AND OVER LAND. +{P(v[0])} DAMAGE.",
            WeaponEffects = v => new[] { Times(AbilityStat.Range, 2f), Flat(Pierce, 99f), Flat(IgnoresLand, 1f), Times(Damage, 1f + v[0]) },
        },
        new("ricochet", "RICOCHET", CardTier.Prismatic)
        {
            AbilityId = LongGun.AbilityId,
            Values = new[] { V(1f, 3f) },
            Describe = v => Whole(v[0]) == 1 ? "A LONG GUN HIT BOUNCES ON TO THE NEAREST OTHER ENEMY WITHIN 10 TILES."
                : $"A LONG GUN HIT BOUNCES ON TO THE NEAREST OTHER ENEMY WITHIN 10 TILES, UP TO {N(v[0])} TIMES.",
            WeaponEffects = v => new[] { Flat(Ricochets, Whole(v[0])) },
        },
        new("fork", "FORK", CardTier.Prismatic)
        {
            AbilityId = LongGun.AbilityId,
            Values = new[] { V(2f, 4f) },
            Describe = v => $"A LONG GUN HIT SPLITS IN TWO, EACH HALF FLYING ON TO ANOTHER ENEMY WITHIN 10 TILES. THE HALVES SPLIT TOO, {N(v[0])} TIMES IN ALL.",
            WeaponEffects = v => new[] { Flat(Forks, Whole(v[0])) },
        },
        new("headhunter", "HEADHUNTER", CardTier.Prismatic)
        {
            AbilityId = LongGun.AbilityId,
            Values = new[] { V(0f, 0.5f) },
            Describe = v => "SINKING A SHIP WITH THE LONG GUN RELOADS IT AT ONCE." + (v[0] < 0.005f ? "" : $" +{P(v[0])} DAMAGE."),
            WeaponEffects = v => new[] { Flat(KillRefund, 1f), Times(Damage, 1f + v[0]) },
        },
        new("carpet-bombing", "CARPET BOMBING", CardTier.Prismatic)
        {
            AbilityId = Mortar.AbilityId,
            Values = new[] { V(6f, 12f) },
            Describe = v => $"THE MORTAR DROPS A LINE OF {N(v[0])} SHELLS FROM YOUR SHIP TO THE TARGET.",
            WeaponEffects = v => new[] { Flat(CarpetShells, Whole(v[0])) },
        },
        new("firestorm", "FIRESTORM", CardTier.Prismatic)
        {
            AbilityId = Mortar.AbilityId,
            Values = new[] { V(3f, 6f), V(10f, 25f) },
            Describe = v => $"SHELLS LEAVE THE WATER BURNING FOR {D(v[0])} SECONDS, {N(v[1])} DAMAGE A SECOND.",
            WeaponEffects = v => new[] { Flat(FireSeconds, v[0]), Flat(FireDps, v[1]) },
        },
    };

    private static readonly Dictionary<string, CardDefinition> ById = All.ToDictionary(c => c.Id);

    public static CardDefinition? Find(string id) => ById.GetValueOrDefault(id);

    public static CardDefinition Get(string id) =>
        ById.TryGetValue(id, out var card) ? card : throw new KeyNotFoundException($"No card '{id}'.");
}
