namespace ShipGame.Shared.Maps;

/// <summary>
/// Names for islands: a word and what the place is ("GULL WATCH", "FORT FLINT", "BONE THRONE", "PORT MERROW"). A
/// fortress sounds as hard as it is: calm ones are lookouts and batteries, rough ones holds and bastions, dire ones
/// citadels and thrones. No word is used twice among the names sharing a <c>taken</c> set (a run's chart, a region's
/// islands), so there's never a "GULL FORT" beside a "GULL ROCK".
/// </summary>
public static class IslandNames
{
    /// <summary>Longest name dealt: it has to fit beside a stop on the chart.</summary>
    public const int MaxLength = 17;

    private static readonly string[] CalmWords =
    {
        "GULL", "TERN", "CORAL", "PELICAN", "KELP", "OYSTER", "HERON", "CONCH", "PALM", "DRIFTWOOD", "LANTERN", "WHISTLE",
        "SEAGRASS", "BRAMBLE", "MARLIN", "COCKLE",
    };

    private static readonly string[] CalmForms = { "{0} WATCH", "{0} POINT", "{0} BATTERY", "{0} LOOKOUT", "{0} REDOUBT" };

    private static readonly string[] RoughWords =
    {
        "BRINE", "CINDER", "RAVEN", "CUTLASS", "TEMPEST", "POWDER", "SHARK", "FLINT", "MUSKET", "THUNDER", "BARRACUDA",
        "SCORCH", "GRAPESHOT", "HURRICANE", "BROADAXE", "IRONWOOD",
    };

    private static readonly string[] RoughForms = { "{0} HOLD", "{0} BASTION", "{0} KEEP", "{0} BULWARK", "FORT {0}" };

    private static readonly string[] DireWords =
    {
        "SKULL", "BONE", "DREAD", "BLOOD", "GALLOWS", "WIDOW", "KRAKEN", "HANGMAN", "BLACKWATER", "CARRION", "GRAVE",
        "SERPENT", "WRAITH", "HELLFIRE", "DROWNED", "REAPER",
    };

    private static readonly string[] DireForms = { "{0} CITADEL", "{0} STRONGHOLD", "{0} SPIRE", "{0} THRONE", "{0} ROCK" };

    private static readonly string[] PortWords =
    {
        "MERROW", "DUSK", "SOLACE", "FAIRWIND", "AMBER", "SAFFRON", "HALCYON", "REVEL", "CALICO", "MARIGOLD", "TALLOW",
        "LIBERTY", "BOUNTY", "JUBILEE",
    };

    private static readonly string[] PortForms = { "PORT {0}", "{0} HARBOR", "{0} HAVEN" };

    private static readonly string[] PlainWords =
    {
        "MANGROVE", "SHIVER", "HOLLOW", "CROOKED", "LITTLE", "SUNKEN", "TURTLE", "PARROT", "NETTLE", "RUM", "CASK",
        "LONGBOAT", "SAND", "MOSS", "SPUR", "ANCHOR", "BARNACLE", "LUBBER",
    };

    private static readonly string[] PlainForms = { "{0} KEY", "{0} CAY", "{0} ISLE", "{0} SHOAL", "{0} REEF", "{0} SPIT" };

    public static string Fortress(Random rng, Difficulty difficulty, ISet<string> taken) => difficulty switch
    {
        Difficulty.Calm => Compose(rng, CalmWords, CalmForms, taken),
        Difficulty.Rough => Compose(rng, RoughWords, RoughForms, taken),
        _ => Compose(rng, DireWords, DireForms, taken),
    };

    public static string Port(Random rng, ISet<string> taken) => Compose(rng, PortWords, PortForms, taken);

    public static string Plain(Random rng, ISet<string> taken) => Compose(rng, PlainWords, PlainForms, taken);

    /// <summary>Marks every word of <paramref name="name"/> taken, so names dealt after it don't echo it.</summary>
    public static void Take(string name, ISet<string> taken)
    {
        foreach (var word in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            taken.Add(word);
    }

    /// <summary>
    /// A word in one of <paramref name="forms"/>, short enough: nothing in it taken if it can be ("TURTLE KEY" after
    /// "RUM CAY"), else a word not yet taken, else any once they're all used.
    /// </summary>
    private static string Compose(Random rng, string[] words, string[] forms, ISet<string> taken)
    {
        var all = words.SelectMany(w => forms.Select(f => (Word: w, Name: string.Format(f, w)))).Where(c => c.Name.Length <= MaxLength).ToList();
        var fresh = all.Where(c => !c.Name.Split(' ').Any(taken.Contains)).ToList();
        var candidates = fresh.Count > 0 ? fresh : all.Where(c => !taken.Contains(c.Word)).ToList() is { Count: > 0 } free ? free : all;
        var name = candidates[rng.Next(candidates.Count)].Name;
        Take(name, taken);
        return name;
    }
}
