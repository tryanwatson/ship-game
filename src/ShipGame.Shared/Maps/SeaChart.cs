namespace ShipGame.Shared.Maps;

/// <summary>What waits at a stop on the chart.</summary>
public enum NodeKind : byte
{
    /// <summary>Where the run sets out from: open water, nothing to fight.</summary>
    Start,

    /// <summary>A fortress to take: sink its every fort and the whole crew chooses cards.</summary>
    Fortress,

    /// <summary>A shipyard in safe water: every hull repaired on arrival, and a shop.</summary>
    Port,

    /// <summary>The act's pirate flagship, which comes hunting soon after the crew arrives.</summary>
    Boss,
}

/// <summary>
/// How hard a fortress is beside the others on its row. Each step is a level: more forts and guards, tougher pirates,
/// and better cards for taking it.
/// </summary>
public enum Difficulty : byte
{
    Calm,
    Rough,
    Dire,
}

/// <summary>One stop on the <see cref="SeaChart"/>.</summary>
/// <param name="Act">From 1. The start is in act 1.</param>
/// <param name="Row">Within its act, from 0; <see cref="SeaChart.RowsPerAct"/> is the boss's. The start's is -1.</param>
/// <param name="Lane">Across the chart, from 0 (left); the start and bosses sit in the middle.</param>
/// <param name="Level">Sets its pirates, its fortress's guns, the cards it deals, and a port's stock.</param>
/// <param name="Next">The stops the crew can sail on to from here; none after the last boss.</param>
/// <param name="Name">A fortress's or port's island, named on the chart before the crew gets there; empty otherwise.</param>
public sealed record ChartNode(int Id, int Act, int Row, int Lane, NodeKind Kind, int Level, Difficulty Difficulty, IReadOnlyList<int> Next,
    string Name = "")
{
    // Records compare lists by reference; compare the courses themselves.
    public bool Equals(ChartNode? other) =>
        other is not null && Id == other.Id && Act == other.Act && Row == other.Row && Lane == other.Lane && Kind == other.Kind
        && Level == other.Level && Difficulty == other.Difficulty && Next.SequenceEqual(other.Next) && Name == other.Name;

    public override int GetHashCode() => HashCode.Combine(Id, Act, Row, Lane, Kind, Level, Difficulty, Next.Count);
}

/// <summary>
/// The run's chart, drawn when it starts: <see cref="Acts"/> acts, each <see cref="RowsPerAct"/> rows of
/// <see cref="Lanes"/> stops and then a boss. The crew sails it one stop at a time, voting for the next. Every stop
/// leads on to the ones in its own lane and the lanes beside it in the next row, so each step is a choice between two
/// or three; the last row of an act all leads to its boss, and a boss to the whole first row of the next act. Every
/// row is fortresses of mixed difficulty (each one level above the last), with a port among the later rows: a fight
/// for cards, or a repair and a shop. The deeper in, the higher every level.
/// </summary>
public sealed class SeaChart
{
    public const int Acts = 3;
    public const int RowsPerAct = 3;
    public const int Lanes = 3;

    /// <summary>The lane in the middle, where the start and the bosses sit.</summary>
    public const int MiddleLane = Lanes / 2;

    /// <summary>Rows of an act (from 0) that have a port in one lane.</summary>
    public static bool HasPort(int row) => row >= 1;

    /// <summary>Act <paramref name="act"/>'s calmest level: 1, 3, 5.</summary>
    public static int BaseLevel(int act) => 1 + 2 * (Math.Clamp(act, 1, Acts) - 1);

    /// <summary>
    /// A fortress's level: its act's, one more in the act's last row, and one more for each step of difficulty. The very
    /// first row is gentler, for crews still unarmed but for a starting card: only Dire is a level up, and Rough is
    /// harder for <see cref="ExtraGuards"/> instead.
    /// </summary>
    public static int FortressLevel(int act, int row, Difficulty difficulty) =>
        BaseLevel(act) + (row >= RowsPerAct - 1 ? 1 : 0) + LevelSteps(act, row, difficulty);

    /// <summary>How many levels a fortress's difficulty adds to it.</summary>
    private static int LevelSteps(int act, int row, Difficulty difficulty) =>
        act <= 1 && row == 0 ? (difficulty == Difficulty.Dire ? 1 : 0) : (int)difficulty;

    /// <summary>Guard ships a fortress gets for each step of difficulty that didn't make it a level higher.</summary>
    public const int GuardsPerStep = 2;

    /// <summary>The guards a fortress's difficulty adds beyond its level's: none, except where a step didn't raise the level.</summary>
    public static int ExtraGuards(int act, int row, Difficulty difficulty) =>
        GuardsPerStep * ((int)difficulty - LevelSteps(act, row, difficulty));

    /// <summary>A port's level, which sets its stock: its act's, and one more for each row in.</summary>
    public static int PortLevel(int act, int row) => BaseLevel(act) + row;

    /// <summary>Boss <paramref name="act"/>'s level: 3, 5, 7.</summary>
    public static int BossLevel(int act) => 1 + 2 * Math.Clamp(act, 1, Acts);

    private readonly ChartNode[] _nodes;

    public SeaChart(IReadOnlyList<ChartNode> nodes)
    {
        if (nodes.Count == 0)
            throw new ArgumentException("A chart needs a start.", nameof(nodes));
        _nodes = nodes.OrderBy(n => n.Id).ToArray();
    }

    /// <summary>Every stop, by id.</summary>
    public IReadOnlyList<ChartNode> Nodes => _nodes;

    /// <summary>Where the crew sets out from.</summary>
    public ChartNode Start => _nodes[0];

    public ChartNode? Find(int id) => Array.Find(_nodes, n => n.Id == id);

    /// <summary>Whether the crew can sail from <paramref name="from"/> straight on to <paramref name="to"/>.</summary>
    public bool Leads(int from, int to) => Find(from)?.Next.Contains(to) == true;

    /// <summary>A fresh chart from <paramref name="seed"/>. Only whole numbers go into it, so it's the same on every machine.</summary>
    public static SeaChart Generate(int seed)
    {
        var rng = new Random(seed);
        var nodes = new List<ChartNode>();
        var nextId = 0;
        var start = nextId++;
        var previous = new[] { start };

        // Build each act's rows first, then wire them up: a node's Next needs the ids of the row after it.
        var drafts = new List<(int Id, int Act, int Row, int Lane, NodeKind Kind, int Level, Difficulty Difficulty)>();
        var rows = new List<int[]>();
        for (var act = 1; act <= Acts; act++)
        {
            for (var row = 0; row < RowsPerAct; row++)
            {
                var portLane = HasPort(row) ? rng.Next(Lanes) : -1;
                var difficulties = Shuffled(rng, Enum.GetValues<Difficulty>());
                var ids = new int[Lanes];
                var fortress = 0;
                for (var lane = 0; lane < Lanes; lane++)
                {
                    ids[lane] = nextId++;
                    if (lane == portLane)
                    {
                        drafts.Add((ids[lane], act, row, lane, NodeKind.Port, PortLevel(act, row), Difficulty.Calm));
                        continue;
                    }
                    var difficulty = difficulties[fortress++];
                    drafts.Add((ids[lane], act, row, lane, NodeKind.Fortress, FortressLevel(act, row, difficulty), difficulty));
                }
                rows.Add(ids);
            }
            var boss = nextId++;
            drafts.Add((boss, act, RowsPerAct, MiddleLane, NodeKind.Boss, BossLevel(act), Difficulty.Dire));
            rows.Add(new[] { boss });
        }

        // Where each row leads: a full row to the one after by lane (its own and those beside it); to or from a single
        // stop (the start, a boss), all of it.
        var next = new Dictionary<int, IReadOnlyList<int>>();
        var all = new List<int[]> { previous };
        all.AddRange(rows);
        for (var i = 0; i < all.Count; i++)
        {
            var from = all[i];
            var to = i + 1 < all.Count ? all[i + 1] : Array.Empty<int>();
            for (var lane = 0; lane < from.Length; lane++)
            {
                next[from[lane]] = from.Length == 1 || to.Length == 1
                    ? to
                    : to.Where((_, l) => Math.Abs(l - lane) <= 1).ToArray();
            }
        }

        // Names from a generator of their own, so they don't shift the chart's shape; none repeats a word in the run.
        var naming = new Random(MixSeed(seed, 0x4E414D45));
        var taken = new HashSet<string>();
        string NameOf(NodeKind kind, Difficulty difficulty) => kind switch
        {
            NodeKind.Fortress => IslandNames.Fortress(naming, difficulty, taken),
            NodeKind.Port => IslandNames.Port(naming, taken),
            _ => "",
        };

        nodes.Add(new ChartNode(start, 1, -1, MiddleLane, NodeKind.Start, 1, Difficulty.Calm, next[start]));
        nodes.AddRange(drafts.Select(d => new ChartNode(d.Id, d.Act, d.Row, d.Lane, d.Kind, d.Level, d.Difficulty, next[d.Id], NameOf(d.Kind, d.Difficulty))));
        return new SeaChart(nodes);
    }

    /// <summary>
    /// A seed for one part of a run drawn from the run's <paramref name="seed"/> and a <paramref name="salt"/>, the same
    /// on every machine and every launch (unlike <see cref="HashCode.Combine{T1,T2}"/>, which is salted per process).
    /// </summary>
    public static int MixSeed(int seed, int salt) => unchecked((seed * 486187739) ^ (salt * 16777619 + 0x5bd1e995));

    private static T[] Shuffled<T>(Random rng, T[] items)
    {
        var shuffled = items.ToArray();
        for (var i = shuffled.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        return shuffled;
    }

    /// <summary>"I", "II", "III": how acts are numbered on the chart.</summary>
    public static string ActNumeral(int act) => act switch
    {
        1 => "I",
        2 => "II",
        3 => "III",
        _ => act.ToString(),
    };

    public static string Name(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Calm => "CALM",
        Difficulty.Rough => "ROUGH",
        _ => "DIRE",
    };

    public static string Name(NodeKind kind) => kind switch
    {
        NodeKind.Start => "OPEN WATER",
        NodeKind.Fortress => "FORTRESS",
        NodeKind.Port => "PORT",
        _ => "FLAGSHIP",
    };
}
