using System.Diagnostics;
using System.Numerics;
using LiteNetLib.Utils;
using ShipGame.Net;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;
using ShipGame.Shared.Upgrades;
using Xunit.Abstractions;

namespace ShipGame.Net.Tests;

/// <summary>
/// Plays ships holding every card, stacked and in random hands, through a fight, and checks nothing breaks: no
/// exceptions, nothing NaN, no stat at zero, no ship that sinks as soon as it respawns, nothing piling up without end,
/// and every snapshot and event batch still round-trips over the wire.
/// </summary>
public class CardFuzzTests(ITestOutputHelper output)
{
    private const int PlayerId = 1;
    private const int Buddy = 2;
    private const int PileLimit = 15000;

    /// <summary>The highest level a card can be held at: its tier's last, and for a prismatic, improved as far as it goes.</summary>
    private static int TopLevel(CardDefinition card) => card.LevelRange.Max + (card.Improves ? CardDefinition.MaxImprovement : 0);

    public static IEnumerable<object[]> EveryCard() => CardCatalog.All.Select(c => new object[] { c.Id });

    [Theory]
    [MemberData(nameof(EveryCard))]
    public void EveryCard_StackedSixDeep_PlaysWithoutBreaking(string id)
    {
        var card = CardCatalog.Get(id);
        var hand = Enumerable.Repeat(new CardPick(id, TopLevel(card), 1f), card.Improves ? 1 : 6).ToList();
        Assert.Empty(Play(hand, seed: id.GetHashCode(), seconds: 8));
    }

    [Fact]
    public void EveryCardAtOnce_WithTheMultipliersStacked_PlaysWithoutBreaking()
    {
        var stacked = new[] { "quick-hands", "rapid-broadsides", "double-battery", "grapeshot", "hot-guns", "skip-shot", "triple-salvo",
            "cluster-bombs", "volley-gun", "mortar-crew", "rifled-barrel", "full-sail" };
        var hand = CardCatalog.All.Where(c => !c.Id.StartsWith("glass-cannon") && c.Id is not ("clipper" or "juggernaut" or "long-gunnery"))
            .Select(c => new CardPick(c.Id, TopLevel(c), 1f))
            .Concat(stacked.SelectMany(id => Enumerable.Repeat(new CardPick(id, TopLevel(CardCatalog.Get(id)), 1f), 4)))
            .ToList();
        Assert.Empty(Play(hand, seed: 7, seconds: 10));
    }

    [Fact]
    public void RandomLateGameHands_PlayWithoutBreaking()
    {
        var problems = new List<string>();
        for (var seed = 0; seed < 25; seed++)
        {
            var rng = new Random(seed);
            var hand = Enumerable.Range(0, 12).Select(_ => CardCatalog.All[rng.Next(CardCatalog.All.Count)])
                .Select(c => new CardPick(c.Id, rng.Next(c.LevelRange.Min, TopLevel(c) + 1), rng.NextSingle())).ToList();
            problems.AddRange(Play(hand, seed, seconds: 6).Select(p => $"[{string.Join(", ", hand.Select(c => c.Id))}] {p}"));
        }
        Assert.Empty(problems);
    }

    private List<string> Play(List<CardPick> hand, int seed, int seconds)
    {
        var problems = new List<string>();
        var world = new World(new Vector2(240, 240)) { Wind = new Vector2(0.3f, 0.1f), FriendlyFire = true };
        world.GetOrAddPlayer(PlayerId);
        world.GetOrAddPlayer(Buddy);
        var ship = world.SpawnShip(new Vector2(120, 120), 0f, ShipStats.Sloop, PlayerId, Loadouts.FullArsenal);
        // Tough enough to last the fight (a percentage cut can still take it to nothing, which is what's checked).
        ship.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 3000f, "fuzz"));
        ship.Health = ship.Stats.MaxHealth;
        // A crewmate far off, so the run never ends with everyone sunk and the player always comes back.
        var buddy = world.SpawnShip(new Vector2(20, 20), 0f, ShipStats.Sloop, Buddy);
        buddy.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 1e6f, "fuzz"));
        buddy.Health = buddy.Stats.MaxHealth;
        buddy.IsAnchored = true;

        var rng = new Random(seed);
        var player = world.Players[PlayerId];
        foreach (var pick in hand)
        {
            player.Cards.Add(pick);
            if (pick.Definition.OnChosen is { } chosen)
                chosen(world, player, pick.Values, rng);
        }
        ship.ReplaceCards(CardStackingHand(hand));
        CheckShip(ship, problems, -1);

        var pirates = new List<Ship>();
        for (var i = 0; i < 30; i++)
        {
            var at = ship.Position + new Vector2(rng.NextSingle() * 50 - 25, rng.NextSingle() * 50 - 25);
            var pirate = world.SpawnShip(at, rng.NextSingle() * MathF.Tau, ShipStats.PirateSloop,
                abilities: new Ability?[] { new BroadsideVolley(), i % 3 == 0 ? new LongGun() : null, i % 5 == 0 ? new Mortar() : null, null });
            pirate.Behavior = new HunterBehavior(at, relentless: true);
            pirate.Throttle = ShipMovement.ThrottleLevels;
            if (i % 4 == 0)
                pirate.AddCard(new CardPick("heated-shot", 3)); // statuses on the player too
            pirates.Add(pirate);
        }

        var watch = new Stopwatch();
        var worstTick = 0.0;
        var spawnedTick = world.Tick;
        int? lastShipId = ship.Id;
        for (var t = 0; t < seconds * SimConstants.TickRate; t++)
        {
            var mine = world.GetPlayerShip(PlayerId);
            if (mine is not null)
            {
                if (mine.Id != lastShipId)
                {
                    spawnedTick = world.Tick;
                    lastShipId = mine.Id;
                }
                if (t % 45 == 0)
                    Anchoring.PressKey(mine);
                if (t % 45 == 40)
                    Anchoring.ReleaseKey(mine);
                mine.Rudder = (t / 20 % 3) - 1;
                if (!mine.IsAnchored)
                    mine.Throttle = ShipMovement.ThrottleLevels;
                var target = world.Ships.Where(s => s.Team == Team.Pirates).MinBy(s => Vector2.DistanceSquared(s.Position, mine.Position));
                for (var slot = AbilitySlot.One; slot <= AbilitySlot.Four; slot++)
                    world.TryCastAbility(mine, slot, target?.Position ?? mine.Position + new Vector2(5, 3));
            }

            watch.Restart();
            try
            {
                world.Step();
            }
            catch (Exception e)
            {
                problems.Add($"tick {t}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                return problems;
            }
            worstTick = Math.Max(worstTick, watch.Elapsed.TotalMilliseconds);

            CheckWire(world, problems, t);
            mine = world.GetPlayerShip(PlayerId);
            if (mine is not null)
                CheckShip(mine, problems, t);
            else if (world.Tick - spawnedTick < SimConstants.TickRate && lastShipId is not null)
            {
                problems.Add($"tick {t}: sank within a second of (re)spawning");
                lastShipId = null;
            }

            foreach (var p in world.Projectiles)
            {
                if (!Finite(p.Position) || !Finite(p.Velocity) || !float.IsFinite(p.Damage))
                {
                    problems.Add($"tick {t}: projectile {p.Id} not finite: {p.Position} {p.Velocity} {p.Damage}");
                    return problems;
                }
            }
            if (world.Projectiles.Count > PileLimit || world.Strikes.Count > PileLimit || world.Fires.Count > PileLimit)
            {
                problems.Add($"tick {t}: piling up: {world.Projectiles.Count} shots, {world.Strikes.Count} shells, {world.Fires.Count} fires");
                return problems;
            }
            if (problems.Count > 0)
                return problems;
        }
        output.WriteLine($"{(hand.Count > 20 ? "EVERYTHING" : string.Join(",", hand.Select(c => c.Id).Distinct()))}: fires {world.Fires.Count}, shells {world.Strikes.Count}, worst tick {worstTick:0.0} ms, {world.Projectiles.Count} shots, "
                         + $"tick {world.Tick}, pirates {world.Ships.Count(x => x.Team == Team.Pirates)}, mine {world.GetPlayerShip(PlayerId)?.Health}, paused {world.IsPaused}");
        if (worstTick > 150)
            problems.Add($"a tick took {worstTick:0} ms");
        return problems;
    }

    private static List<CardPick> CardStackingHand(List<CardPick> hand)
    {
        var held = new List<CardPick>();
        foreach (var pick in hand.Where(p => !p.Definition.OneShot))
            CardStacking.Add(held, pick);
        return held;
    }

    private static void CheckShip(Ship ship, List<string> problems, int t)
    {
        var s = ship.Stats;
        if (!Finite(ship.Position) || !float.IsFinite(ship.Heading) || !float.IsFinite(ship.Speed) || !float.IsFinite(ship.Health))
            problems.Add($"tick {t}: ship not finite: {ship.Position} {ship.Heading} {ship.Speed} {ship.Health}");
        foreach (var (name, value) in new[]
                 {
                     ("max health", s.MaxHealth), ("max speed", s.MaxSpeed), ("damage", s.WeaponDamage), ("reload speed", s.CooldownSpeed),
                     ("range", s.WeaponRange), ("shot speed", s.ProjectileSpeed), ("turn radius", s.MinTurnRadius),
                 })
        {
            if (!(value > 0f) || !float.IsFinite(value))
                problems.Add($"tick {t}: {name} is {value}");
        }
        foreach (var ability in ship.Abilities.OfType<AbilityState>())
        {
            for (var c = 0; c < ability.Channels; c++)
            {
                if (ability.DurationTicks(c) is < 0 or > 30 * 60)
                    problems.Add($"tick {t}: {ability.Definition.Id} reload of {ability.DurationTicks(c)} ticks");
            }
        }
    }

    private static void CheckWire(World world, List<string> problems, int t)
    {
        var events = world.DrainEvents();
        var w = new NetDataWriter();
        w.PutEvents(events);
        var back = new NetDataReader(w.CopyData()).GetEvents();
        if (back.Count != events.Count)
            problems.Add($"tick {t}: {events.Count} events came back as {back.Count}");
        if (t % 2 == 0)
        {
            foreach (var chunk in Wire.WriteSnapshotChunks(Snapshot.Capture(world)))
            {
                if (chunk.Length > Protocol.MaxSnapshotChunkBytes)
                    problems.Add($"tick {t}: a snapshot chunk of {chunk.Length} bytes");
            }
        }
    }

    private static bool Finite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
}
