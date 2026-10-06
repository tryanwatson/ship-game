using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Tests;

/// <summary>A testing run: a late game's worth of cards chosen up front, from hands of one tier, rerolled for free.</summary>
public class TestingRunTests
{
    private static World Open(int players = 1, int startingGold = 0) =>
        Runs.Create(seed: 4, Enumerable.Range(1, players).Select(id => (id, $"sailor {id}")).ToList(), startingGold: startingGold, testing: true);

    private static IEnumerable<CardDefinition> Hand(World world, int playerId = 1) =>
        world.Players[playerId].CardOffers[0].Cards.Select(c => c.Definition);

    [Fact]
    public void ALateGamesWorth_IsTheStartingCard_EveryFortress_AndEveryBossButTheLast()
    {
        Assert.Equal(9, CardRewards.TestingHands);
    }

    [Fact]
    public void EveryPlayer_IsDealtThatManyHands_OfFourPrismatics_AtTheirStrongest()
    {
        var world = Open(players: 2);

        Assert.True(world.IsPaused);
        foreach (var player in world.Players.Values)
        {
            Assert.True(player.NeedsStartingWeapon);
            Assert.Equal(CardRewards.TestingHands, player.CardOffers.Count);
            Assert.All(player.CardOffers, offer =>
            {
                Assert.Equal(OfferSource.Testing, offer.Source);
                Assert.Equal(CardRewards.TopOfferSize, offer.Cards.Count);
                Assert.All(offer.Cards, c => Assert.Equal(CardTier.Prismatic, c.Definition.Tier));
                Assert.All(offer.Cards, c => Assert.Equal(c.Definition.LevelRange.Max, c.Level));
            });
        }
    }

    [Theory]
    [InlineData(CardTier.Silver)]
    [InlineData(CardTier.Gold)]
    [InlineData(CardTier.Prismatic)]
    public void Rerolls_DealTheTierAskedFor_ForNothing_AsOftenAsYouLike(CardTier tier)
    {
        var world = Open();
        for (var i = 0; i < 20; i++)
        {
            var before = Hand(world).Select(c => c.Id).ToList();
            world.Enqueue(new RerollCardsCommand(1, tier));
            world.Step();

            Assert.Empty(world.DrainEvents().OfType<CommandRejected>());
            Assert.Equal(CardRewards.TopOfferSize, Hand(world).Count());
            Assert.All(Hand(world), c => Assert.Equal(tier, c.Tier));
            Assert.Empty(Hand(world).Select(c => c.Id).Intersect(before));
        }
        Assert.Equal(0, world.Players[1].Gold);
        Assert.Equal(0, world.Players[1].Rerolls); // the price of a real reroll later is untouched
        Assert.Equal(CardRewards.TestingHands, world.Players[1].CardOffers.Count);
    }

    [Fact]
    public void HavingChosenThemAll_ThePlayerPicksAWeapon_AndSetsSailWithEveryCard()
    {
        var world = Open();
        for (var i = 0; i < CardRewards.TestingHands; i++)
        {
            world.Enqueue(new RerollCardsCommand(1, i % 2 == 0 ? CardTier.Gold : CardTier.Prismatic));
            world.Step();
            world.Enqueue(new ChooseCardCommand(1, world.Players[1].CardOffers[0].Cards[0].Id));
            world.Step();
        }
        Assert.Empty(world.Players[1].CardOffers);
        Assert.True(world.IsPaused); // the weapon's still to come

        world.Enqueue(new ChooseStartingWeaponCommand(1, BroadsideVolley.AbilityId));
        world.Step();

        Assert.False(world.IsPaused);
        Assert.Equal(CardRewards.TestingHands, world.DrainEvents().OfType<CardChosen>().Count()); // a prismatic taken twice is one, improved
        Assert.NotEmpty(world.GetPlayerShip(1)!.Cards);
        Assert.Equal(0, world.Director!.FortressesTaken);
    }

    [Fact]
    public void ATier_IsIgnored_ForAnOrdinaryHand_WhichStillCostsGold()
    {
        var world = Runs.Create(seed: 4, new[] { (1, "ANNE") }, startingGold: CardRewards.RerollBaseCost);

        world.Enqueue(new RerollCardsCommand(1, CardTier.Prismatic));
        world.Step();

        Assert.Equal(0, world.Players[1].Gold);
        Assert.Equal(1, world.Players[1].Rerolls);
        Assert.Equal(OfferSource.Start, world.Players[1].CardOffers[0].Source);
        Assert.Equal(CardRewards.OfferSize, Hand(world).Count());
    }
}
