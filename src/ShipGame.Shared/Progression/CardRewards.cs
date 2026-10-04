using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Progression;

/// <summary>What earned an offer of cards.</summary>
public enum OfferSource
{
    /// <summary>The free card every run opens with.</summary>
    Start,

    Fortress,

    /// <summary>A boss sunk (one that didn't end the run).</summary>
    Boss,
}

/// <summary>
/// Cards on offer to one player: what earned them, the level they were dealt at (which set their tiers and numbers),
/// and any free rerolls left.
/// </summary>
public sealed record CardOffer(OfferSource Source, int Level, IReadOnlyList<CardPick> Cards, int FreeRerolls = 0)
{
    // Records compare lists by reference; compare the cards themselves.
    public bool Equals(CardOffer? other) =>
        other is not null && Source == other.Source && Level == other.Level && FreeRerolls == other.FreeRerolls && Cards.SequenceEqual(other.Cards);

    public override int GetHashCode() => HashCode.Combine(Source, Level, FreeRerolls, Cards.Count);

    public bool Contains(string cardId) => Cards.Any(c => c.Id == cardId);
}

/// <summary>
/// Cards as rewards. Taking a fortress wins every player in the run an offer of their own, dealt at the fortress's
/// level: the level sets the odds of each tier (<see cref="TierOdds"/>), how many cards there are, and the numbers on
/// them, so the hardest fortresses deal hands of prismatics at their strongest. Sinking a boss (other than the last)
/// deals a hand of prismatics, stronger for the second than the first. Every run also opens with a free starting card.
/// Weapon cards are only dealt for weapons the player carries (any, before they've chosen one). The game pauses until
/// everyone has chosen (see <see cref="World.IsPaused"/>), however long that takes; offers queue up if they come at
/// once. A player chooses with <c>ChooseCardCommand</c>, afloat or not, and can pay to reroll (<see cref="TryReroll"/>).
/// </summary>
public static class CardRewards
{
    public const int OfferSize = 3;

    /// <summary>The level-8 hand: one more to choose from.</summary>
    public const int TopOfferSize = 4;

    /// <summary>The first reroll's price; every reroll a player makes doubles the price of their next, for the whole run.</summary>
    public const int RerollBaseCost = 50;

    /// <summary>What a player who has already rerolled <paramref name="rerolls"/> times pays for the next: 50, 100, 200, 400...</summary>
    public static int RerollCost(int rerolls) => RerollBaseCost * (1 << Math.Clamp(rerolls, 0, 20));

    /// <summary>The level a boss's hand is dealt at: round 1 at 6, round 2 at 8.</summary>
    public static int BossDropLevel(int round) => Math.Min(8, 4 + 2 * Math.Max(1, round));

    /// <summary>Chances of silver, gold and prismatic for each card in an offer.</summary>
    public static (float Silver, float Gold, float Prismatic) TierOdds(OfferSource source, int level) => source switch
    {
        OfferSource.Start => (0.70f, 0.30f, 0f),
        OfferSource.Boss => (0f, 0f, 1f),
        _ => level switch
        {
            <= 2 => (0.60f, 0.40f, 0f),
            <= 4 => (0.25f, 0.60f, 0.15f),
            <= 6 => (0.05f, 0.55f, 0.40f),
            7 => (0f, 0.40f, 0.60f),
            _ => (0f, 0f, 1f),
        },
    };

    public static int CardsFor(OfferSource source, int level) => source == OfferSource.Fortress && level >= 8 ? TopOfferSize : OfferSize;

    public static int FreeRerollsFor(OfferSource source, int level) => source == OfferSource.Fortress && level >= 7 ? 1 : 0;

    /// <summary>A few words on what a fortress of <paramref name="level"/> pays out, for the map and the island's label.</summary>
    public static string RewardLabel(int level) => level switch
    {
        <= 2 => "SILVER",
        <= 4 => "GOLD",
        <= 6 => "GOLD+",
        7 => "PRISMATIC",
        _ => "PRISMATIC X4",
    };

    /// <summary>Offers each player in the run their own hand.</summary>
    public static void OfferAll(World world, Random rng, OfferSource source, int level)
    {
        foreach (var player in world.Players.Values.OrderBy(p => p.PlayerId).ToList())
            Offer(world, player.PlayerId, Deal(rng, Eligible(world, player), source, level));
    }

    /// <summary>
    /// Cards that suit the player's ship (its weapons; the ship they lost, while they wait to respawn). Before they've
    /// a weapon at all (the starting card), every card: a good weapon card can decide which weapon to start with.
    /// </summary>
    public static IReadOnlyList<CardDefinition> Eligible(World world, PlayerState player)
    {
        var ship = world.GetPlayerShip(player.PlayerId) ?? player.LostShip;
        var armed = ship?.Abilities.Any(a => a is not null) == true;
        return CardCatalog.All.Where(c => c.AbilityId is null || !armed || ship!.HasAbility(c.AbilityId)).ToList();
    }

    /// <summary>
    /// A hand from <paramref name="pool"/>: each card's tier drawn by <see cref="TierOdds"/>, all different, at
    /// <paramref name="level"/> (or the top of its tier, if that's lower). A tier with nothing left to deal gives way to
    /// the next tier down, then up. Cards in <paramref name="avoid"/> are dealt only if there's nothing else.
    /// </summary>
    public static CardOffer Deal(Random rng, IReadOnlyList<CardDefinition> pool, OfferSource source, int level, IReadOnlyCollection<string>? avoid = null)
    {
        var odds = TierOdds(source, level);
        var dealt = new List<CardPick>();
        var size = CardsFor(source, level);
        for (var i = 0; i < size; i++)
        {
            var roll = rng.NextSingle();
            var tier = roll < odds.Silver ? CardTier.Silver : roll < odds.Silver + odds.Gold ? CardTier.Gold : CardTier.Prismatic;
            var left = pool.Where(c => dealt.All(d => d.Id != c.Id)).ToList();
            var fresh = avoid is null ? left : left.Where(c => !avoid.Contains(c.Id)).ToList();
            var card = Pick(rng, fresh, tier) ?? Pick(rng, left, tier);
            if (card is null)
                break;
            var (_, top) = card.LevelRange;
            dealt.Add(new CardPick(card.Id, Math.Min(level, top), card.IsRolled ? rng.NextSingle() : 0f));
        }
        return new CardOffer(source, level, dealt, FreeRerollsFor(source, level));
    }

    private static CardDefinition? Pick(Random rng, IReadOnlyList<CardDefinition> left, CardTier tier)
    {
        foreach (var t in new[] { tier, tier - 1, tier - 2, tier + 1, tier + 2 })
        {
            var ofTier = left.Where(c => c.Tier == t).ToList();
            if (ofTier.Count > 0)
                return ofTier[rng.Next(ofTier.Count)];
        }
        return null;
    }

    /// <summary>Puts an offer at the back of the player's queue and announces it.</summary>
    public static void Offer(World world, int playerId, CardOffer offer)
    {
        if (offer.Cards.Count == 0)
            return;
        world.GetOrAddPlayer(playerId).CardOffers.Add(offer);
        world.Emit(new CardsOffered(world.Tick, playerId, offer));
    }

    /// <summary>
    /// Takes <paramref name="cardId"/> from the player's oldest offer: the card is theirs (and their ship's, if afloat;
    /// a prismatic they already hold improves instead), anything it does on the spot happens, and the offer is gone.
    /// Null on success.
    /// </summary>
    public static RejectionReason? TryChoose(World world, int playerId, string cardId)
    {
        if (!world.Players.TryGetValue(playerId, out var player) || player.CardOffers.Count == 0)
            return RejectionReason.NoCardOffer;
        var offer = player.CardOffers[0];
        if (!offer.Contains(cardId) || CardCatalog.Find(cardId) is not { } card)
            return RejectionReason.NoCardOffer;
        var pick = offer.Cards.First(c => c.Id == cardId);

        player.CardOffers.RemoveAt(0);
        CardStacking.Add(player.Cards, pick);
        world.GetPlayerShip(playerId)?.ReplaceCards(player.Cards);
        card.OnChosen?.Invoke(world, player, card.ValuesFor(pick), world.Director?.Rng ?? new Random((int)world.Tick * 31 + playerId));
        world.Emit(new CardChosen(world.Tick, playerId, pick));
        return null;
    }

    /// <summary>
    /// Swaps the player's oldest offer for a fresh hand of the same kind and level, of other cards where there are
    /// enough to go round. A free reroll the offer came with is used first; after that it costs
    /// <see cref="RerollCost"/>, which doubles each time. Null on success.
    /// </summary>
    public static RejectionReason? TryReroll(World world, int playerId)
    {
        if (!world.Players.TryGetValue(playerId, out var player) || player.CardOffers.Count == 0)
            return RejectionReason.NoCardOffer;
        var current = player.CardOffers[0];
        var free = current.FreeRerolls > 0;
        var cost = RerollCost(player.Rerolls);
        if (!free && player.Gold < cost)
            return RejectionReason.NotEnoughGold;

        var rng = world.Director?.Rng ?? new Random((int)world.Tick * 31 + playerId * 7 + player.Rerolls);
        var dealt = Deal(rng, Eligible(world, player), current.Source, current.Level, current.Cards.Select(c => c.Id).ToList());
        var rerolled = dealt with { FreeRerolls = free ? current.FreeRerolls - 1 : current.FreeRerolls };
        if (!free)
        {
            world.AddGold(playerId, -cost);
            player.Rerolls++;
        }
        player.CardOffers[0] = rerolled;
        world.Emit(new CardsRerolled(world.Tick, playerId, rerolled, player.Rerolls));
        return null;
    }

    /// <summary>Mirrors a reroll the server announced.</summary>
    public static void ApplyRerolled(World world, int playerId, CardOffer offer, int rerolls)
    {
        var player = world.GetOrAddPlayer(playerId);
        if (player.CardOffers.Count > 0)
            player.CardOffers[0] = offer;
        player.Rerolls = rerolls;
    }

    /// <summary>Mirrors a choice the server announced: the oldest offer goes, and the card joins the player's hand.</summary>
    public static void ApplyChosen(World world, int playerId, CardPick pick)
    {
        var player = world.GetOrAddPlayer(playerId);
        if (player.CardOffers.Count > 0)
            player.CardOffers.RemoveAt(0);
        if (CardCatalog.Find(pick.Id) is not null)
            CardStacking.Add(player.Cards, pick);
    }
}
