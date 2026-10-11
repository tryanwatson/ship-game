using System.Globalization;
using System.Numerics;
using ShipGame.BalanceSim;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;

// Headless balance sim: bot crews (see PlayerBot) fight the real fortresses and bosses through the run director, so
// garrisons, relief fleets, boss phases, escorts, respawns and lifeboats all play as in a run. It prints a table and
// checks it against the targets in Targets below.
//
//   dotnet run -c Release --project tools/ShipGame.BalanceSim -- [--seeds 4] [--only fort|boss] [--dodge 0.6] [--csv out.csv]
//
// Bots are no substitute for playtests: they don't anchor, kite or focus fire. Read the numbers as a floor on a
// decent player, and trust the comparisons (level to level, crew to crew) more than any one figure.

var options = Options.Parse(args);
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
var weapons = new[] { BroadsideVolley.AbilityId, LongGun.AbilityId, Mortar.AbilityId };
var crews = new[] { 1, 2, 4, 8 };

// Which kit a crew holds against what: a fortress of each level at the stage of the run it's met, and each boss.
var fortressPlan = new (Kit Kit, int[] Levels)[]
{
    (Kit.Act1, new[] { 1, 2 }),
    (Kit.Act2, new[] { 3, 4, 5 }),
    (Kit.Act3, new[] { 5, 6, 7, 8 }),
};
var bossPlan = new (Kit Kit, int Round)[] { (Kit.Act2, 1), (Kit.Act3, 2), (Kit.Final, 3) };

var results = new List<Result>();
foreach (var crew in crews)
{
    var mixes = crew == 1 ? weapons.Select(w => new[] { w }).ToList() : new List<string[]> { Enumerable.Range(0, crew).Select(i => weapons[i % 3]).ToArray() };
    foreach (var mix in mixes)
    {
        if (options.Only is null or "fort")
            foreach (var (kit, levels) in fortressPlan)
                foreach (var level in levels)
                    for (var seed = 0; seed < options.Seeds; seed++)
                        results.Add(Fight.Fortress(level, kit, mix, seed, options.Dodge));
        if (options.Only is null or "boss")
            foreach (var (kit, round) in bossPlan)
                for (var seed = 0; seed < options.Seeds; seed++)
                    results.Add(Fight.Boss(round, kit, mix, seed, options.Dodge));
    }
}

if (options.Csv is { } csv)
{
    File.WriteAllLines(csv, results.Select(r => r.Csv).Prepend(Result.CsvHeader));
    Console.WriteLine($"Wrote {results.Count} fights to {csv}.");
}
Report.Print(results);
return Report.Check(results) ? 0 : 1;

internal sealed record Options(int Seeds, string? Only, float Dodge, string? Csv)
{
    public static Options Parse(string[] args)
    {
        var options = new Options(Seeds: 4, Only: null, Dodge: 0.6f, Csv: null);
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            options = args[i] switch
            {
                "--seeds" => options with { Seeds = int.Parse(args[i + 1]) },
                "--only" => options with { Only = args[i + 1] },
                "--dodge" => options with { Dodge = float.Parse(args[i + 1], CultureInfo.InvariantCulture) },
                "--csv" => options with { Csv = args[i + 1] },
                _ => throw new ArgumentException($"Unknown option {args[i]}."),
            };
        }
        return options;
    }
}

/// <summary>What a player plausibly holds at each stage of a run.</summary>
internal enum Kit
{
    /// <summary>Act 1: the starting card and nothing bought.</summary>
    Act1,

    /// <summary>Act 2: three cards, two skills, three levels of four upgrades.</summary>
    Act2,

    /// <summary>Act 3: six cards (two prismatic), the full tree, six levels of six upgrades.</summary>
    Act3,

    /// <summary>The last boss: nine cards, everything bought.</summary>
    Final,
}

internal sealed record Result(string Kind, string Target, Kit Kit, int Crew, string Weapons, int Seed, string Outcome, float Seconds,
    int Deaths, float DamageTakenPercent, float DpsPerPlayer, float EnemyHealth, int Enemies)
{
    public const string CsvHeader = "kind,target,kit,crew,weapons,seed,outcome,seconds,deaths,dmgTakenPctMaxHp,dpsPerPlayer,enemyHealth,enemies";

    public string Csv =>
        $"{Kind},{Target},{Kit},{Crew},{Weapons},{Seed},{Outcome},{Seconds:0.0},{Deaths},{DamageTakenPercent:0},{DpsPerPlayer:0.0},{EnemyHealth:0},{Enemies}";
}

internal static class Fight
{
    private const int MaxSeconds = 400;
    private const int ChartStart = 0;
    private const int ChartStop = 1;

    public static Result Fortress(int level, Kit kit, string[] mix, int seed, float dodge)
    {
        // A calm fortress of this level, in the middle row of the act it's usually met in (no extra guards).
        var act = level <= 2 ? 1 : level <= 4 ? 2 : 3;
        var node = new ChartNode(ChartStop, act, 1, SeaChart.MiddleLane, NodeKind.Fortress, level, Difficulty.Calm, Array.Empty<int>());
        var world = Sail(node, kit, mix, seed, dodge);
        return Play(world, "fort", $"L{level}", kit, mix, seed, dodge, timeFromBoss: false);
    }

    public static Result Boss(int round, Kit kit, string[] mix, int seed, float dodge)
    {
        var node = new ChartNode(ChartStop, round, SeaChart.RowsPerAct, SeaChart.MiddleLane, NodeKind.Boss, SeaChart.BossLevel(round),
            Difficulty.Dire, Array.Empty<int>());
        var world = Sail(node, kit, mix, seed, dodge);
        return Play(world, "boss", $"B{round}", kit, mix, seed, dodge, timeFromBoss: true);
    }

    /// <summary>A crew in <paramref name="kit"/>, sailed by the director straight into <paramref name="node"/>'s sea.</summary>
    private static World Sail(ChartNode node, Kit kit, string[] mix, int seed, float dodge)
    {
        var world = new World(Regions.StartSize);
        var director = new RunDirector(seed * 7919 + node.Level * 31 + mix.Length);
        world.Director = director;
        director.Chart = new SeaChart(new[]
        {
            new ChartNode(ChartStart, 1, -1, SeaChart.MiddleLane, NodeKind.Start, 1, Difficulty.Calm, new[] { ChartStop }),
            node,
        });
        for (var i = 0; i < mix.Length; i++)
        {
            var id = i + 1;
            var player = world.GetOrAddPlayer(id);
            var weapon = WeaponCatalog.Find(mix[i])!;
            var ship = world.SpawnShip(world.WorldSize / 2f, Regions.EntryHeading, ShipStats.Sloop, id, Loadouts.Starting(weapon.Ability));
            foreach (var modifier in weapon.StartingBonus)
                ship.AddModifier(modifier);
            Kits.Fit(ship, player, mix[i], kit);
            ship.Health = ship.Stats.MaxHealth;
        }
        Runs.GrantLifeboats(world);
        director.SailTo(world, ChartStop);
        Crew(world, seed, dodge);
        return world;
    }

    /// <summary>Every player ship without a bot gets one (each new one after a respawn, too).</summary>
    private static void Crew(World world, int seed, float dodge)
    {
        foreach (var ship in world.Ships)
        {
            if (ship.OwnerPlayerId is { } id && ship.Behavior is null)
                ship.Behavior = new PlayerBot(seed * 131 + id * 17 + (int)world.Tick, dodge);
        }
    }

    private static Result Play(World world, string kind, string target, Kit kit, string[] mix, int seed, float dodge, bool timeFromBoss)
    {
        var director = world.Director!;
        var maxHealth = world.Ships.Where(s => s.OwnerPlayerId is not null).Average(s => s.Stats.MaxHealth);
        var lastShips = new Dictionary<int, Ship>();
        var deaths = 0;
        long? started = timeFromBoss ? null : world.Tick;
        float enemyHealth = 0f;
        var enemyIds = new HashSet<int>();
        var outcome = "timeout";
        var limit = world.Tick + (long)MaxSeconds * SimConstants.TickRate;
        while (world.Tick < limit)
        {
            foreach (var ship in world.Ships)
            {
                if (ship.OwnerPlayerId is { } owner)
                    lastShips[owner] = ship;
                else if (ship.Team == Team.Pirates && enemyIds.Add(ship.Id))
                    enemyHealth += ship.Stats.MaxHealth; // everything it met, relief fleets and escorts included
            }
            world.Step();
            foreach (var e in world.DrainEvents())
            {
                if (e is PlayerSunk)
                    deaths++;
                else if (e is BossSpawned)
                    started ??= world.Tick;
            }
            Crew(world, seed, dodge);
            if (world.IsRunOver)
            {
                outcome = world.IsVictory ? "win" : "wipe";
                break;
            }
            if (director.Cleared)
            {
                outcome = "win";
                break;
            }
        }

        var taken = 0f;
        var dealt = 0f;
        foreach (var player in world.Players.Values)
        {
            var ship = world.GetPlayerShip(player.PlayerId) ?? player.LostShip ?? lastShips.GetValueOrDefault(player.PlayerId);
            taken += ship?.TallyOf(Tally.DamageTaken) ?? 0f;
            dealt += ship?.TallyOf(Tally.DamageDealt) ?? 0f;
        }
        var seconds = (world.Tick - (started ?? world.Tick)) / (float)SimConstants.TickRate;
        var crew = mix.Length;
        return new Result(kind, target, kit, crew, string.Join("+", mix.Distinct()), seed, outcome, seconds, deaths,
            taken / crew / (float)maxHealth * 100f, dealt / crew / MathF.Max(1f, seconds), enemyHealth, enemyIds.Count);
    }
}

internal static class Kits
{
    public static void Fit(Ship ship, PlayerState player, string weapon, Kit kit)
    {
        var cards = new List<CardPick> { new("heavy-shot", 1) };
        var (gold1, gold2, gold3, prismatic1, prismatic2) = weapon switch
        {
            BroadsideVolley.AbilityId => ("double-battery", "powder-kegs", "rapid-broadsides", "twin-decks", "man-o-war"),
            LongGun.AbilityId => ("big-bore", "rifled-barrel", "volley-gun", "ricochet", "fork"),
            _ => ("triple-salvo", "big-shells", "mortar-crew", "carpet-bombing", "firestorm"),
        };
        var skills = weapon switch
        {
            BroadsideVolley.AbilityId => new[] { "rapid-guns", "improved-powder", "rolling-thunder" },
            LongGun.AbilityId => new[] { "heavy-shot", "piercing-shot", "hullbreaker" },
            _ => new[] { "heavy-shell", "cluster-shell", "earthshaker" },
        };
        var (levels, upgrades, skillCount) = kit switch
        {
            Kit.Act1 => (0, Array.Empty<string>(), 0),
            Kit.Act2 => (3, new[] { "hull", "reload", "damage", "repairs" }, 2),
            Kit.Act3 => (6, new[] { "hull", "reload", "damage", "repairs", "range", "speed" }, 3),
            _ => (8, new[] { "hull", "reload", "damage", "repairs", "range", "speed" }, 3),
        };
        if (kit >= Kit.Act2)
            cards.AddRange(new CardPick[] { new(gold1, 3), new("ironclad", 3) });
        if (kit >= Kit.Act3)
            cards.AddRange(new CardPick[] { new(gold2, 5), new(prismatic1, 6), new("quick-hands", 5) });
        if (kit >= Kit.Final)
            cards.AddRange(new CardPick[] { new(gold3, 7), new(prismatic2, 8), new("heavy-shot", 7) });
        foreach (var card in cards)
            CardStacking.Add(player.Cards, card);
        ship.ReplaceCards(player.Cards);
        foreach (var skill in skills.Take(skillCount))
            ship.AddSkill(SkillTrees.Find(skill)!);
        foreach (var upgrade in upgrades)
            for (var level = 0; level < levels; level++)
                ship.AddModifier(UpgradeCatalog.Find(upgrade)!.Modifier);
    }
}

/// <summary>What the numbers should look like (from the 2026-10-10 balance review), and the table.</summary>
internal static class Report
{
    /// <summary>Hull lost per fortress, per player, as a percent of their max: a fight that costs something, early and late.</summary>
    private static (float Low, float High) HullLost(Kit kit) => kit switch
    {
        Kit.Act1 => (40f, 70f),
        Kit.Act2 => (30f, 60f),
        _ => (20f, 50f),
    };

    /// <summary>Seconds a boss lasts once it arrives.</summary>
    private static (float Low, float High) BossSeconds(string round) => round switch
    {
        "B1" => (45f, 75f),
        "B2" => (60f, 90f),
        _ => (90f, 150f),
    };

    /// <summary>A crew's hull lost per player should stay within this much of a lone sailor's.</summary>
    private const float CrewParity = 0.25f;

    private static float Median(IEnumerable<float> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? float.NaN : sorted[sorted.Count / 2];
    }

    private static IEnumerable<IGrouping<(string Kind, string Target, Kit Kit, int Crew, string Weapons), Result>> Groups(List<Result> results) =>
        results.GroupBy(r => (r.Kind, r.Target, r.Kit, r.Crew, r.Weapons));

    public static void Print(List<Result> results)
    {
        Console.WriteLine($"{"fight",-5} {"vs",-4} {"kit",-6} {"crew",4} {"weapons",-26} {"win",4} {"wipe",4} {"slow",4} {"secs",6} {"deaths",6} {"hull%",6} {"dps",7} {"enemyHP",8} {"ships",5}");
        foreach (var g in Groups(results))
        {
            var v = g.ToList();
            var wins = v.Where(r => r.Outcome == "win").ToList();
            Console.WriteLine($"{g.Key.Kind,-5} {g.Key.Target,-4} {g.Key.Kit,-6} {g.Key.Crew,4} {g.Key.Weapons,-26} {wins.Count,4} {v.Count(r => r.Outcome == "wipe"),4} "
                              + $"{v.Count(r => r.Outcome == "timeout"),4} {Median(wins.Select(r => r.Seconds)),6:0} {v.Average(r => r.Deaths),6:0.0} "
                              + $"{Median(v.Select(r => r.DamageTakenPercent)),6:0} {Median(v.Select(r => r.DpsPerPlayer)),7:0.0} {Median(v.Select(r => r.EnemyHealth)),8:0} {Median(v.Select(r => (float)r.Enemies)),5:0}");
        }
    }

    /// <summary>Prints every target missed; true if none were.</summary>
    public static bool Check(List<Result> results)
    {
        var misses = new List<string>();
        foreach (var g in Groups(results))
        {
            var v = g.ToList();
            var name = $"{g.Key.Kind} {g.Key.Target} {g.Key.Kit} x{g.Key.Crew} {g.Key.Weapons}";
            if (g.Key.Kind == "fort" && g.Key.Crew == 1)
            {
                var (low, high) = HullLost(g.Key.Kit);
                var hull = Median(v.Select(r => r.DamageTakenPercent));
                if (hull < low || hull > high)
                    misses.Add($"{name}: hull lost {hull:0}% (target {low:0}-{high:0}%)");
            }
            if (g.Key.Kind == "boss")
            {
                var (low, high) = BossSeconds(g.Key.Target);
                var seconds = Median(v.Where(r => r.Outcome == "win").Select(r => r.Seconds));
                if (!(seconds >= low && seconds <= high))
                    misses.Add($"{name}: lasts {seconds:0}s (target {low:0}-{high:0}s)");
            }
        }

        // Crews against the solo average at the same fight.
        foreach (var g in results.Where(r => r.Kind == "fort").GroupBy(r => (r.Target, r.Kit)))
        {
            var solo = Median(g.Where(r => r.Crew == 1).Select(r => r.DamageTakenPercent));
            foreach (var crew in g.Where(r => r.Crew > 1).GroupBy(r => r.Crew))
            {
                var hull = Median(crew.Select(r => r.DamageTakenPercent));
                if (solo > 0f && MathF.Abs(hull / solo - 1f) > CrewParity)
                    misses.Add($"fort {g.Key.Target} {g.Key.Kit} x{crew.Key}: hull lost {hull:0}% vs {solo:0}% solo (target within {CrewParity:P0})");
            }
        }

        Console.WriteLine();
        Console.WriteLine(misses.Count == 0 ? "Every target met." : $"{misses.Count} targets missed:");
        foreach (var miss in misses)
            Console.WriteLine("  " + miss);
        return misses.Count == 0;
    }
}
