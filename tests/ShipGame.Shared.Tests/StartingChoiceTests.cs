using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Tests;

/// <summary>How a run opens: each player names a free starting card, then the weapon to set sail with.</summary>
public class StartingChoiceTests
{
    private static World Open(int players = 2, int startingGold = 0) =>
        Runs.Create(seed: 4, Enumerable.Range(1, players).Select(id => (id, $"sailor {id}")).ToList(), startingGold: startingGold);

    private static RejectionReason? Rejection(World world)
    {
        var rejected = world.DrainEvents().OfType<CommandRejected>().ToList();
        return rejected.Count == 0 ? null : Assert.Single(rejected).Reason;
    }

    [Fact]
    public void ARun_OpensPaused_WithEveryoneUnarmed_AndOfferedAStartingCard()
    {
        var world = Open();

        Assert.True(world.IsPaused);
        foreach (var player in world.Players.Values)
        {
            Assert.True(player.NeedsStartingWeapon);
            Assert.Equal(CardRewards.OfferSize, Assert.Single(player.CardOffers).Cards.Count);
            Assert.All(world.GetPlayerShip(player.PlayerId)!.Abilities, Assert.Null);
        }
        Assert.Equal("SAILOR 1", world.Players[1].Name); // as shown, in the pixel font's capitals
        Assert.Equal(0, world.Director!.FortressesTaken); // the starting card counts toward no boss
    }

    [Fact]
    public void TheStartingCard_CanBeAnyCard_WeaponCardsIncluded()
    {
        var world = Open(players: 1);
        var eligible = CardRewards.Eligible(world, world.Players[1]);

        Assert.Equal(CardCatalog.All.Count, eligible.Count);
        var seen = Enumerable.Range(0, 40).SelectMany(seed => Runs.Create(seed, new[] { (1, "ANNE") }).Players[1].CardOffers[0].Cards)
            .Select(c => c.Definition.AbilityId).Distinct().ToList();
        Assert.Contains(Mortar.AbilityId, seen);
        Assert.Contains(LongGun.AbilityId, seen);
    }

    [Fact]
    public void TheWeapon_ComesAfterTheCard_AndThenTheRunStarts()
    {
        var world = Open();
        world.DrainEvents();
        var tick = world.Tick;

        world.Enqueue(new ChooseStartingWeaponCommand(1, Mortar.AbilityId));
        world.Step();
        Assert.Equal(RejectionReason.ChooseCardFirst, Rejection(world));

        foreach (var player in world.Players.Values)
            world.Enqueue(new ChooseCardCommand(player.PlayerId, player.CardOffers[0].Cards[0].Id));
        world.Enqueue(new ChooseStartingWeaponCommand(1, "cutlass"));
        world.Step();
        Assert.Equal(RejectionReason.UnknownWeapon, Rejection(world));
        Assert.True(world.IsPaused); // still waiting on the weapons

        world.Enqueue(new ChooseStartingWeaponCommand(1, Mortar.AbilityId));
        world.Enqueue(new ChooseStartingWeaponCommand(2, LongGun.AbilityId));
        world.Step();
        Assert.False(world.IsPaused);
        var events = world.DrainEvents();
        Assert.Equal(2, events.OfType<StartingWeaponChosen>().Count());
        var ship = world.GetPlayerShip(1)!;
        Assert.IsType<Mortar>(ship.GetAbility(AbilitySlot.One)!.Definition);
        Assert.All(ship.Abilities.Skip(1), Assert.Null);
        Assert.Single(ship.Cards);

        world.Step();
        Assert.Equal(tick + 1, world.Tick); // under way
        world.Enqueue(new ChooseStartingWeaponCommand(1, BroadsideVolley.AbilityId));
        world.Step();
        Assert.Equal(RejectionReason.NotChoosingWeapon, Rejection(world));
    }

    [Fact]
    public void TheStartingCard_CanBeRerolled_WithStartingGold()
    {
        var world = Open(players: 1, startingGold: CardRewards.RerollBaseCost);
        var before = world.Players[1].CardOffers[0].Cards.Select(c => c.Id).ToList();

        world.Enqueue(new RerollCardsCommand(1));
        world.Step();

        Assert.Equal(0, world.Players[1].Gold);
        Assert.Empty(world.Players[1].CardOffers[0].Cards.Select(c => c.Id).Intersect(before));
    }

    [Theory]
    [InlineData("anne bonny", "ANNE BONNY")]
    [InlineData("  calico   jack!! ", "CALICO JACK")]
    [InlineData("a very long pirate name indeed", "A VERY LONG PI")]
    [InlineData("??", "")]
    public void Names_AreTidied_ForThePixelFont(string given, string shown)
    {
        Assert.Equal(shown, PlayerNames.Clean(given));
    }

    [Fact]
    public void EachStartingWeapon_BringsItsBonus_ForTheWholeShip()
    {
        var world = Open(players: 3);
        foreach (var player in world.Players.Values)
            world.Enqueue(new ChooseCardCommand(player.PlayerId, player.CardOffers[0].Cards[0].Id));
        world.Step();
        var picks = new[] { WeaponCatalog.Broadside, WeaponCatalog.LongGun, WeaponCatalog.Mortar };
        for (var i = 0; i < picks.Length; i++)
            world.Enqueue(new ChooseStartingWeaponCommand(i + 1, picks[i].Id));
        world.Step();

        for (var i = 0; i < picks.Length; i++)
        {
            var ship = world.GetPlayerShip(i + 1)!;
            Assert.Equal(picks[i].StartingBonus, ship.Modifiers.Where(m => m.Source == WeaponCatalog.StartingBonusSource));
            Assert.Equal(ship.Stats.MaxHealth, ship.Health); // tougher from the outset, not damaged
        }
        var broadside = world.GetPlayerShip(1)!;
        Assert.True(broadside.Stats.MaxHealth > ShipStats.Sloop.MaxHealth * 1.4f);
        Assert.True(broadside.Stats.HealthRegen > ShipStats.Sloop.HealthRegen * 4f);
    }
}
