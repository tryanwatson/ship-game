using System.Numerics;
using System.Text.RegularExpressions;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Tests;

/// <summary>
/// Every card's text, at every level and roll, on every kind of ship (no guns yet, each weapon, sailing, rowing, its
/// tallies grown part way): it should read like a card, never like a bug.
/// </summary>
public class CardTextTests
{
    // Things no card should ever say: a broken number, a zero where a bonus should be, a lone 1 with a plural.
    private static readonly Regex Garbled = new(
        @"NAN|INFINITY|NULL|[{}]|[+-]0(\.0)?%|\+0 |-0\b|\b0 (BALLS|SECONDS|DAMAGE|TILES|SHELLS|TIMES|SHIPS|CANNON|DEGREES|GOLD)\b"
        + @"|\b1 (KILLS|BROADSIDE HITS|LONG GUN HITS|MORTAR HITS|TILES|SECONDS|SHELLS|TIMES|SHIPS|BALLS|MORE SHELLS|MORE SHOTS)\b"
        + @"|  |\.\.|\s$|^\s",
        RegexOptions.Compiled);

    private static IEnumerable<(string Name, Ship Ship)> Ships()
    {
        var world = new World(new Vector2(300, 300));
        var x = 10f;
        Ship Spawn(IReadOnlyList<Ability?> loadout) => world.SpawnShip(new Vector2(x += 10f, 50), 0f, ShipStats.Sloop, (int)x, loadout);

        yield return ("no guns yet", Spawn(new Ability?[Ship.AbilitySlotCount]));
        yield return ("broadside", Spawn(Loadouts.Starting(new BroadsideVolley())));
        yield return ("long gun", Spawn(Loadouts.Starting(new LongGun())));
        yield return ("mortar", Spawn(Loadouts.Starting(new Mortar())));
        var sailing = Spawn(Loadouts.FullArsenal);
        sailing.Speed = 2.3f;
        yield return ("sailing", sailing);
        var rowing = Spawn(Loadouts.FullArsenal);
        rowing.Speed = -0.8f;
        yield return ("rowing astern", rowing);
        foreach (var amount in new[] { 1f, 49.5f, 250f, 1234.5f })
        {
            var grown = Spawn(Loadouts.FullArsenal);
            foreach (var tally in Enum.GetValues<Tally>())
                grown.AddToTally(tally, amount);
            yield return ($"tallies at {amount}", grown);
        }
    }

    [Fact]
    public void EveryCard_ReadsCleanly_AtEveryLevelAndRoll_OnEveryShip()
    {
        var ships = Ships().ToList();
        var problems = new List<string>();
        foreach (var card in CardCatalog.All)
        {
            for (var level = 1; level <= card.LevelRange.Max + CardDefinition.MaxImprovement; level++)
            {
                foreach (var roll in new[] { 0f, 0.5f, 1f })
                {
                    var pick = new CardPick(card.Id, level, roll);
                    Check($"{card.Id} L{level} r{roll}", pick.Description);
                    foreach (var (name, ship) in ships)
                    {
                        if (pick.DescriptionOn(ship) is { } on)
                            Check($"{card.Id} L{level} on {name}", on);
                    }
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(20)));

        void Check(string where, string text)
        {
            if (string.IsNullOrWhiteSpace(text) || Garbled.IsMatch(text) || text != text.ToUpperInvariant() || !text.EndsWith('.') && !text.EndsWith('?'))
                problems.Add($"{where}: \"{text}\"");
        }
    }

    [Fact]
    public void ManOWar_SaysNothingOfARing_OnAShipWithNoBroadside()
    {
        var ships = Ships().ToDictionary(s => s.Name, s => s.Ship);
        var pick = new CardPick("man-o-war", 5);

        Assert.Null(pick.DescriptionOn(ships["no guns yet"])); // choosing a starting card
        Assert.Null(pick.DescriptionOn(ships["long gun"]));
        Assert.StartsWith("YOUR RING: ", pick.DescriptionOn(ships["broadside"]));
    }

    [Fact]
    public void Ram_GivesTheDamageAtTopSpeed_AndAtTheSpeedYoureGoing()
    {
        var ships = Ships().ToDictionary(s => s.Name, s => s.Ship);
        var pick = new CardPick("ram", 3);

        Assert.Equal("AT YOUR TOP SPEED: 40 DAMAGE.", pick.DescriptionOn(ships["broadside"])); // stopped: just the top
        Assert.Equal("AT YOUR TOP SPEED: 40 DAMAGE. AT YOUR SPEED NOW: 18.", pick.DescriptionOn(ships["sailing"]));
    }

    [Fact]
    public void AGrowingCard_SaysHowFarItsGrown_AndWhatsNext()
    {
        var ships = Ships().ToDictionary(s => s.Name, s => s.Ship);
        var pick = new CardPick("bounty-hunter", 1);

        Assert.Equal("EVERY 5 KILLS: +2% DAMAGE WITH EVERY WEAPON.", pick.Description);
        Assert.Equal("NOT GROWN YET. FIRST STEP IN 5 KILLS.", pick.DescriptionOn(ships["broadside"]));
        Assert.Equal("NOT GROWN YET. FIRST STEP IN 4 KILLS.", pick.DescriptionOn(ships["tallies at 1"]));
        Assert.Equal("GROWN TO +18% DAMAGE WITH EVERY WEAPON. NEXT STEP IN 1 KILL.", pick.DescriptionOn(ships["tallies at 49.5"]));
    }

    [Fact]
    public void OnlyAPrismaticTakenAgain_GoesPastItsTiersTop()
    {
        foreach (var card in CardCatalog.All)
        {
            var top = new CardPick(card.Id, card.LevelRange.Max, 1f).Values;
            var past = new CardPick(card.Id, card.LevelRange.Max + CardDefinition.MaxImprovement, 1f).Values;
            if (card.Improves)
                Assert.True(top.Zip(past).Any(p => p.First != p.Second) || top.Length == 0, $"{card.Name} doesn't improve when taken again");
            else
                Assert.Equal(top, past); // a testing hand at level 8 deals a silver at its best, not past it
        }
    }
}
