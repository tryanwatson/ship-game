using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Tests;

/// <summary>Cards: their tiers and numbers, how they're dealt and chosen, and that they stay with the player.</summary>
public class CardTests
{
    private const int PlayerId = 1;

    private static (World world, Ship ship) CreateWorld(params Ability[] weapons)
    {
        var world = new World(new Vector2(200, 200)) { Wind = Vector2.Zero };
        var loadout = weapons.Length == 0 ? Loadouts.FullArsenal : weapons.Cast<Ability?>().Concat(new Ability?[4]).Take(4).ToArray();
        var ship = world.SpawnShip(new Vector2(100, 100), 0f, ShipStats.Sloop, PlayerId, loadout);
        return (world, ship);
    }

    private static CardOffer Hand(int level, params string[] ids) =>
        new(OfferSource.Fortress, level, ids.Select(id => new CardPick(id, level)).ToArray());

    private static void Offer(World world, params string[] ids) => CardRewards.Offer(world, PlayerId, Hand(1, ids));

    private static void Choose(World world, string cardId, int level = 1, int playerId = PlayerId)
    {
        CardRewards.Offer(world, playerId, Hand(level, cardId));
        world.Enqueue(new ChooseCardCommand(playerId, cardId));
        world.Step();
    }

    private static float[] ValuesAt(string id, int level, float roll = 0f) => new CardPick(id, level, roll).Values;

    // ---- The catalog ------------------------------------------------------------------------------------

    [Fact]
    public void EveryCard_DoesSomething_ToAWeaponThatExists()
    {
        Assert.Equal(CardCatalog.All.Count, CardCatalog.All.Select(c => c.Id).Distinct().Count());
        foreach (var card in CardCatalog.All)
        {
            var pick = new CardPick(card.Id, card.LevelRange.Max, 0.5f);
            var effects = card.StatModifiersFor(pick).Count() + card.StatModifiersFor(pick, atAnchor: true).Count()
                          + card.AbilityModifiersFor(pick).Count() + card.PerksFor(pick).Count();
            Assert.True(effects > 0 || card.OneShot, $"{card.Name} does nothing");
            Assert.Equal(card.WeaponEffects is not null, card.AbilityId is not null);
            if (card.AbilityId is { } weapon)
                Assert.NotNull(WeaponCatalog.Find(weapon));
            Assert.Equal(card.Name.ToUpperInvariant(), card.Name);
            foreach (var level in new[] { card.LevelRange.Min, card.LevelRange.Max })
            {
                var description = new CardPick(card.Id, level, 0.5f).Description;
                Assert.Equal(description.ToUpperInvariant(), description);
            }
        }
    }

    [Fact]
    public void EveryTier_HasCardsForAnyShip_AndForEachWeapon()
    {
        foreach (var tier in Enum.GetValues<CardTier>())
        {
            var cards = CardCatalog.All.Where(c => c.Tier == tier).ToList();
            Assert.True(cards.Count(c => c.AbilityId is null) >= CardRewards.TopOfferSize, $"{tier} needs a full hand for any ship");
            Assert.All(WeaponCatalog.All, w => Assert.Contains(cards, c => c.AbilityId == w.Id));
        }
    }

    [Fact]
    public void Numbers_RunLowToHigh_AcrossTheirTiersLevels()
    {
        Assert.Equal(3f, ValuesAt("cluster-bombs", 1)[0], 3);           // gold: levels 1-7
        Assert.Equal(10f, ValuesAt("cluster-bombs", 7)[0], 3);
        Assert.Equal(0.01f, ValuesAt("shipwrights", 1)[0], 4);
        Assert.Equal(0.05f, ValuesAt("shipwrights", 7)[0], 4);
        Assert.Equal(0.8f, ValuesAt("glass-cannon", 6)[0], 3);          // silver: levels 1-6
        Assert.Equal(6f, ValuesAt("carpet-bombing", 3)[0], 3);          // prismatic: levels 3-8
        Assert.Equal(12f, ValuesAt("carpet-bombing", 8)[0], 3);
        Assert.Equal(90f, ValuesAt("second-wind", 3)[1], 3);            // a cooldown that shrinks as it improves
        Assert.Equal(45f, ValuesAt("second-wind", 8)[1], 3);
        Assert.Equal("EVERY SHELL SCATTERS 10 EXTRA EXPLOSIONS.", new CardPick("cluster-bombs", 7).Description);
    }

    [Fact]
    public void RolledNumbers_FallInTheirRange_ForTheLevel()
    {
        Assert.True(CardCatalog.Get("treasure-map").IsRolled);
        Assert.Equal(100f, ValuesAt("treasure-map", 1, roll: 0f)[0], 3);
        Assert.Equal(200f, ValuesAt("treasure-map", 1, roll: 1f)[0], 3);
        Assert.Equal(600f, ValuesAt("treasure-map", 7, roll: 0.5f)[0], 3);
        Assert.Equal(400f, ValuesAt("treasure-map", 7, roll: 0f)[0], 3);
    }

    // ---- Dealing ----------------------------------------------------------------------------------------

    [Fact]
    public void EveryLevel_DealsABetterHand_ThanTheOneBelow()
    {
        static double Expected(OfferSource source, int level) =>
            CardRewards.Hand(source, level).Sum(s => (int)s.Tier + s.Upgrade);

        for (var level = 2; level <= 8; level++)
            Assert.True(Expected(OfferSource.Fortress, level) > Expected(OfferSource.Fortress, level - 1), $"level {level} deals no better than {level - 1}");
        Assert.All(CardRewards.Hand(OfferSource.Boss, 6), s => Assert.Equal(CardTier.Prismatic, s.Tier));
        Assert.All(Enumerable.Range(1, 8).SelectMany(l => CardRewards.Hand(OfferSource.Fortress, l)),
            s => Assert.True(s.Upgrade == 0f || s.Tier < CardTier.Prismatic, "a prismatic can't come any better"));
        Assert.Equal((CardRewards.TopOfferSize, 1), (CardRewards.CardsFor(OfferSource.Fortress, 8), CardRewards.FreeRerollsFor(OfferSource.Fortress, 8)));
        Assert.Equal((CardRewards.OfferSize, 1), (CardRewards.CardsFor(OfferSource.Fortress, 7), CardRewards.FreeRerollsFor(OfferSource.Fortress, 7)));
        Assert.Equal(0, CardRewards.FreeRerollsFor(OfferSource.Fortress, 6));
        Assert.Equal(new[]
            {
                "1 GOLD + 2 SILVER", "2 GOLD + 1 SILVER, 25% CHANCE OF A PRISMATIC", "1 PRISMATIC + 2 GOLD, 50% CHANCE OF ANOTHER PRISMATIC",
                "4 PRISMATIC + A FREE REROLL",
            },
            new[] { 1, 3, 6, 8 }.Select(CardRewards.RewardLabel));
    }

    [Fact]
    public void AHand_IsDealtAsItsSlotsSay()
    {
        // The sure cards always come as their tier; the chancy one comes better about as often as it says.
        var rng = new Random(5);
        var hands = Enumerable.Range(0, 2000).Select(_ => CardRewards.Deal(rng, CardCatalog.All, OfferSource.Fortress, 3).Cards).ToList();
        Assert.All(hands, cards => Assert.Equal(new[] { CardTier.Silver, CardTier.Gold }, cards.Take(2).Select(c => c.Definition.Tier)));
        var better = hands.Count(cards => cards[2].Definition.Tier == CardTier.Prismatic) / (double)hands.Count;
        Assert.InRange(better, 0.2, 0.3);
    }

    [Fact]
    public void ALevel8Hand_IsFourPrismatics_AtTheirStrongest()
    {
        var offer = CardRewards.Deal(new Random(2), CardCatalog.All, OfferSource.Fortress, 8);

        Assert.Equal(CardRewards.TopOfferSize, offer.Cards.Count);
        Assert.All(offer.Cards, c => Assert.Equal((CardTier.Prismatic, 8), (c.Definition.Tier, c.Level)));
        Assert.Equal(1, offer.FreeRerolls);
    }

    [Fact]
    public void AHandShortOfATier_IsFilledFromTheNextOne_AtItsTopLevel()
    {
        var pool = new[] { CardCatalog.Get("echo"), CardCatalog.Get("ram"), CardCatalog.Get("full-sail"), CardCatalog.Get("heavy-shot") };

        var offer = CardRewards.Deal(new Random(1), pool, OfferSource.Fortress, 8);

        Assert.Equal(4, offer.Cards.Count);
        Assert.Equal(2, offer.Cards.Count(c => c.Definition.Tier == CardTier.Prismatic));
        Assert.All(offer.Cards.Where(c => c.Definition.Tier == CardTier.Gold), c => Assert.Equal(7, c.Level)); // gold's best
    }

    // ---- Choosing ---------------------------------------------------------------------------------------

    [Fact]
    public void ChoosingACard_PlaysIt_AtTheLevelItWasDealt()
    {
        var (world, ship) = CreateWorld();
        CardRewards.Offer(world, PlayerId, Hand(7, "full-sail", "ironclad", "heavy-shot"));
        world.DrainEvents();

        world.Enqueue(new ChooseCardCommand(PlayerId, "full-sail"));
        world.Step();

        Assert.Equal(ShipStats.Sloop.MaxSpeed * 1.7f, ship.Stats.MaxSpeed, 3);
        Assert.Equal(ShipStats.Sloop.MaxHealth, ship.Stats.MaxHealth);
        Assert.Empty(world.Players[PlayerId].CardOffers);
        Assert.Equal(new CardPick("full-sail", 7), Assert.Single(world.Players[PlayerId].Cards));
        Assert.Equal(new CardPick("full-sail", 7), Assert.Single(ship.Cards));
        // Chosen during the pause, which stops the clock.
        Assert.Equal(new CardChosen(world.Tick, PlayerId, new CardPick("full-sail", 7)), Assert.Single(world.DrainEvents().OfType<CardChosen>()));
    }

    [Fact]
    public void ACardNotOnOffer_IsRefused()
    {
        var (world, ship) = CreateWorld();
        world.Enqueue(new ChooseCardCommand(PlayerId, "full-sail"));
        world.Step();
        Assert.Equal(RejectionReason.NoCardOffer, Assert.Single(world.DrainEvents().OfType<CommandRejected>()).Reason);

        // Offers are answered oldest first: the second's cards wait their turn.
        Offer(world, "ironclad");
        Offer(world, "full-sail");
        world.Enqueue(new ChooseCardCommand(PlayerId, "full-sail"));
        world.Step();

        Assert.Equal(RejectionReason.NoCardOffer, Assert.Single(world.DrainEvents().OfType<CommandRejected>()).Reason);
        Assert.Empty(ship.Cards);
        Assert.Equal(2, world.Players[PlayerId].CardOffers.Count);
    }

    [Fact]
    public void SilverAndGold_Stack_WhenTakenAgain()
    {
        var (world, ship) = CreateWorld();
        Choose(world, "full-sail");
        Choose(world, "full-sail");

        Assert.Equal(ShipStats.Sloop.MaxSpeed * 1.6f, ship.Stats.MaxSpeed, 3);
        Assert.Equal(2, ship.Cards.Count);
    }

    [Fact]
    public void APrismatic_TakenAgain_Improves()
    {
        var (world, ship) = CreateWorld();
        Choose(world, "echo", level: 5);
        var once = ship.PerkValue(Perk.Echo);
        Assert.Equal(new CardPick("echo", 6), CardStacking.WouldBecome(ship.Cards, new CardPick("echo", 3)));

        Choose(world, "echo", level: 3);

        Assert.Equal(new CardPick("echo", 6), Assert.Single(ship.Cards));
        Assert.True(ship.PerkValue(Perk.Echo) > once);
        Choose(world, "echo", level: 8);
        Choose(world, "echo", level: 8);
        Assert.Equal(10, Assert.Single(ship.Cards).Level); // past the tier's top: better than any dealt
        Assert.True(ship.PerkValue(Perk.Echo) > ValuesAt("echo", 8)[0]);
    }

    [Fact]
    public void Cards_StayWithThePlayer_ThroughASinking_EvenOnesChosenWhileSunk()
    {
        var world = new World(new Vector2(200, 200)) { Wind = Vector2.Zero };
        var ship = world.SpawnShip(new Vector2(100, 100), 0f, ShipStats.Sloop, PlayerId, Loadouts.FullArsenal);
        world.SpawnShip(new Vector2(60, 60), 0f, ShipStats.Sloop, PlayerId + 1).IsAnchored = true; // someone to come back to
        Choose(world, "ironclad");

        ship.Health = 0f;
        world.Step();
        Assert.Null(world.GetPlayerShip(PlayerId));
        Choose(world, "full-sail"); // from the bottom of the sea

        for (var i = 0; i < Respawning.DelayTicks + 1; i++)
            world.Step();

        var reborn = world.GetPlayerShip(PlayerId)!;
        Assert.Equal(new[] { "ironclad", "full-sail" }, reborn.Cards.Select(c => c.Id));
        Assert.Equal(ShipStats.Sloop.MaxHealth * 1.6f, reborn.Stats.MaxHealth, 3);
        Assert.InRange(reborn.Health, reborn.Stats.MaxHealth * Respawning.ReturnHealth, reborn.Stats.MaxHealth * Respawning.ReturnHealth + 1f);
        Assert.Equal(ShipStats.Sloop.MaxSpeed * 1.3f, reborn.Stats.MaxSpeed, 3);
    }

    [Fact]
    public void Cards_StackWithUpgrades()
    {
        var (world, ship) = CreateWorld();
        ship.AddModifier(new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 20f, "upgrade:hull"));
        Choose(world, "ironclad", level: 7);

        Assert.Equal((ShipStats.Sloop.MaxHealth + 20f) * 2.5f, ship.Stats.MaxHealth, 2);
        Assert.Equal(ship.Stats.MaxHealth, ship.Health); // the new health comes topped up
        Assert.DoesNotContain(ship.Modifiers, m => m.Source.StartsWith("card:")); // cards travel as picks, not modifiers
    }

    // ---- One-shots ----------------------------------------------------------------------------------------

    [Fact]
    public void ATreasureMap_PaysItsRolledGold_OnTheSpot()
    {
        var (world, _) = CreateWorld();
        CardRewards.Offer(world, PlayerId, new CardOffer(OfferSource.Fortress, 7, new[] { new CardPick("treasure-map", 7, 0.5f) }));
        world.Enqueue(new ChooseCardCommand(PlayerId, "treasure-map"));
        world.Step();

        Assert.Equal(600, world.Players[PlayerId].Gold);
    }

    [Fact]
    public void AFreeArmory_GivesAWeaponTheShipLacks_ThenSkills()
    {
        var (world, ship) = CreateWorld(new BroadsideVolley());
        Choose(world, "free-armory", level: 5);
        Assert.Equal(2, ship.Abilities.Count(a => a is not null));

        Choose(world, "free-armory", level: 5);
        Choose(world, "free-armory", level: 5);
        Assert.All(WeaponCatalog.All, w => Assert.True(ship.HasAbility(w.Id)));
        Assert.Single(ship.Skills); // with every weapon aboard, a skill
    }

    // ---- The pause ----------------------------------------------------------------------------------------

    [Fact]
    public void WhileCardsAreOnOffer_TheWorldStandsStill()
    {
        var (world, ship) = CreateWorld(new LongGun(), new Mortar());
        ship.Throttle = ShipMovement.ThrottleLevels;
        world.Step();
        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + new Vector2(10, 0));
        world.TryCastAbility(ship, AbilitySlot.Two, ship.Position + new Vector2(0, 10));
        var (tick, position, shot, cooldown) = (world.Tick, ship.Position, world.Projectiles.Single().Position,
            ship.GetAbility(AbilitySlot.One)!.CooldownRemainingTicks);
        Offer(world, "full-sail", "ironclad", "heavy-shot");
        Assert.True(world.IsPaused);

        for (var i = 0; i < SimConstants.TickRate * 5; i++)
            world.Step();

        Assert.Equal(tick, world.Tick);
        Assert.Equal(position, ship.Position);
        Assert.Equal(position, ship.PreviousPosition); // nothing to blend: it holds still on screen too
        Assert.Equal(shot, world.Projectiles.Single().Position);
        Assert.Single(world.Strikes); // still in the air
        Assert.Equal(cooldown, ship.GetAbility(AbilitySlot.One)!.CooldownRemainingTicks);
    }

    [Fact]
    public void WhilePaused_GunsAreRefused_ButTheHelmIsTaken()
    {
        var (world, ship) = CreateWorld(new BroadsideVolley());
        Offer(world, "full-sail");
        world.DrainEvents();

        world.Enqueue(new CastAbilityCommand(PlayerId, AbilitySlot.One, ship.Position + new Vector2(0, 5)));
        world.Enqueue(new AdjustThrottleCommand(PlayerId, 2));
        world.Step();

        Assert.Empty(world.Projectiles);
        Assert.Equal(RejectionReason.Paused, Assert.Single(world.DrainEvents().OfType<CommandRejected>()).Reason);
        Assert.Equal(2, ship.Throttle);
    }

    [Fact]
    public void ThePause_LastsUntilEveryoneHasChosen_HoweverLong()
    {
        var (world, _) = CreateWorld();
        world.SpawnShip(new Vector2(50, 50), 0f, ShipStats.Sloop, PlayerId + 1);
        Offer(world, "full-sail");
        CardRewards.Offer(world, PlayerId + 1, Hand(1, "ironclad"));
        var tick = world.Tick;

        world.Enqueue(new ChooseCardCommand(PlayerId, "full-sail"));
        for (var i = 0; i < SimConstants.TickRate * 120; i++)
            world.Step();
        Assert.True(world.IsPaused); // two minutes on, still waiting for the other player
        Assert.Equal(tick, world.Tick);

        world.Enqueue(new ChooseCardCommand(PlayerId + 1, "ironclad"));
        world.Step();
        Assert.False(world.IsPaused);
        world.Step();
        Assert.Equal(tick + 1, world.Tick);
    }

    [Fact]
    public void APlayerLeaving_TakesTheirOfferWithThem()
    {
        var (world, _) = CreateWorld();
        world.SpawnShip(new Vector2(50, 50), 0f, ShipStats.Sloop, PlayerId + 1);
        CardRewards.Offer(world, PlayerId + 1, Hand(1, "ironclad"));
        Assert.True(world.IsPaused);

        world.RemovePlayer(PlayerId + 1);

        Assert.False(world.IsPaused);
    }

    // ---- Rerolls -----------------------------------------------------------------------------------------

    [Fact]
    public void Rerolling_CostsGold_DoublingEachTime_ForTheWholeRun()
    {
        Assert.Equal(new[] { 50, 100, 200, 400, 800 }, Enumerable.Range(0, 5).Select(CardRewards.RerollCost));

        var (world, _) = CreateWorld();
        world.AddGold(PlayerId, 1000);
        Offer(world, "full-sail", "ironclad", "heavy-shot");

        world.Enqueue(new RerollCardsCommand(PlayerId));
        world.Step();
        world.Enqueue(new RerollCardsCommand(PlayerId));
        world.Step();
        Assert.Equal(1000 - 50 - 100, world.Players[PlayerId].Gold);

        // The doubling carries over to the next fortress's offer.
        world.Enqueue(new ChooseCardCommand(PlayerId, world.Players[PlayerId].CardOffers[0].Cards[0].Id));
        world.Step();
        Offer(world, "full-sail", "ironclad", "heavy-shot");
        world.Enqueue(new RerollCardsCommand(PlayerId));
        world.Step();
        Assert.Equal(1000 - 50 - 100 - 200, world.Players[PlayerId].Gold);
        Assert.Equal(3, world.Players[PlayerId].Rerolls);
    }

    [Fact]
    public void ARerolledOffer_IsFreshCards_OfTheSameLevel_AndTheGameStaysPaused()
    {
        var (world, _) = CreateWorld();
        world.AddGold(PlayerId, CardRewards.RerollBaseCost);
        var before = new[] { "full-sail", "ironclad", "heavy-shot" };
        CardRewards.Offer(world, PlayerId, Hand(5, before));
        world.DrainEvents();

        world.Enqueue(new RerollCardsCommand(PlayerId));
        world.Step();

        var after = world.Players[PlayerId].CardOffers.Single();
        Assert.Equal(5, after.Level);
        Assert.Equal(CardRewards.OfferSize, after.Cards.Select(c => c.Id).Distinct().Count());
        Assert.Empty(after.Cards.Select(c => c.Id).Intersect(before));
        Assert.True(world.IsPaused);
        var events = world.DrainEvents();
        Assert.Equal(after, Assert.Single(events.OfType<CardsRerolled>()).Offer);
        Assert.Equal(-CardRewards.RerollBaseCost, Assert.Single(events.OfType<GoldChanged>()).Delta);
    }

    [Fact]
    public void AFreeReroll_IsUsedFirst_AndCostsNothing()
    {
        var (world, _) = CreateWorld();
        world.AddGold(PlayerId, CardRewards.RerollBaseCost);
        CardRewards.Offer(world, PlayerId, CardRewards.Deal(new Random(3), CardCatalog.All, OfferSource.Fortress, 7));
        Assert.Equal(1, world.Players[PlayerId].CardOffers[0].FreeRerolls);

        world.Enqueue(new RerollCardsCommand(PlayerId));
        world.Step();
        Assert.Equal(CardRewards.RerollBaseCost, world.Players[PlayerId].Gold);
        Assert.Equal((0, 0), (world.Players[PlayerId].CardOffers[0].FreeRerolls, world.Players[PlayerId].Rerolls));

        world.Enqueue(new RerollCardsCommand(PlayerId));
        world.Step();
        Assert.Equal(0, world.Players[PlayerId].Gold); // then it's paid for
    }

    [Fact]
    public void Rerolling_IsRefused_WithoutTheGold_OrAnOffer()
    {
        var (world, _) = CreateWorld();
        world.Enqueue(new RerollCardsCommand(PlayerId));
        world.Step();
        Assert.Equal(RejectionReason.NoCardOffer, Assert.Single(world.DrainEvents().OfType<CommandRejected>()).Reason);

        world.AddGold(PlayerId, CardRewards.RerollBaseCost - 1);
        var offer = Hand(1, "full-sail", "ironclad", "heavy-shot");
        CardRewards.Offer(world, PlayerId, offer);
        world.DrainEvents();
        world.Enqueue(new RerollCardsCommand(PlayerId));
        world.Step();

        Assert.Equal(RejectionReason.NotEnoughGold, Assert.Single(world.DrainEvents().OfType<CommandRejected>()).Reason);
        Assert.Equal(offer, world.Players[PlayerId].CardOffers.Single());
        Assert.Equal(CardRewards.RerollBaseCost - 1, world.Players[PlayerId].Gold);
    }

    // ---- What weapon cards do -----------------------------------------------------------------------------

    [Fact]
    public void ReloadCards_Compound_RatherThanCancellingOut()
    {
        var (world, ship) = CreateWorld(new Mortar());
        ship.AddSkill(SkillTrees.All.First(s => s.AbilityId == Mortar.AbilityId
                                                && s.Effects.Any(e => e is { Stat: AbilityStat.Cooldown, Kind: ModifierKind.Percent })));
        var withSkill = new Mortar().CooldownTicksFor(ship);

        Choose(world, "mortar-crew", level: 7); // 65% faster
        Assert.Equal(withSkill / 1.65f, new Mortar().CooldownTicksFor(ship), 2);
        Choose(world, "mortar-crew", level: 7);
        Assert.Equal(withSkill / 1.65f / 1.65f, new Mortar().CooldownTicksFor(ship), 2);
    }

    [Fact]
    public void DoubleBattery_AddsCannon_ByLevel()
    {
        var (world, ship) = CreateWorld(new BroadsideVolley());
        Choose(world, "double-battery", level: 7);

        world.TryCastAbility(ship, AbilitySlot.One, ship.Position + new Vector2(0, 5));

        Assert.Equal(BroadsideVolley.CannonCount + 6, world.Projectiles.Count);
    }
}
