using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;
using ShipGame.Shared.Trading;

namespace ShipGame.Shared.Tests;

public class TradingTests
{
    private const int Trader = 1;
    private const int Rival = 2;

    // A trading post at x 40..48 and an ordinary island 60 tiles east of it; ships anchor 3 tiles off a west shore.
    private static readonly Vector2 PostAnchorage = new(37, 30);
    private static readonly Vector2 DestinationAnchorage = new(97, 30);
    private static readonly Vector2 OpenSea = new(70, 80);

    private static Island Square(int id, float left, bool shipyard) =>
        new(id, new[] { new Vector2(left, 26), new Vector2(left + 8, 26), new Vector2(left + 8, 34), new Vector2(left, 34) },
            hasShipyard: shipyard);

    private static World TwoIslands(int gold = 1000)
    {
        var world = new World(new Vector2(160, 128)) { Wind = Vector2.Zero };
        world.AddIsland(Square(1, 40, shipyard: true));
        world.AddIsland(Square(2, 100, shipyard: false));
        Contracts.OpenMarkets(world, seed: 7);
        var ship = world.SpawnShip(PostAnchorage, 0f, ShipStats.Sloop, Trader, Loadouts.Sloop);
        world.Players[Trader].Gold = gold;
        Anchor(world, ship);
        return world;
    }

    private static void Anchor(World world, Ship ship)
    {
        ship.IsAnchored = true;
        world.Step();
    }

    private static void MoveTo(World world, Ship ship, Vector2 position)
    {
        ship.IsAnchored = false;
        ship.Position = ship.PreviousPosition = position;
        Anchor(world, ship);
    }

    private static Ship ShipOf(World world, int player) => world.GetPlayerShip(player)!;

    private static int Gold(World world, int player = Trader) => world.Players[player].Gold;

    private static TradeContract Offer(World world, int index = 0) => world.Trade.OffersAt(1)[index];

    private static IReadOnlyList<WorldEvent> Buy(World world, int contractId, int player = Trader)
    {
        world.DrainEvents();
        world.Enqueue(new PurchaseContractCommand(player, contractId));
        world.Step();
        return world.DrainEvents();
    }

    private static void Sink(World world, Ship ship)
    {
        ship.Health = 0;
        world.Step();
    }

    [Fact]
    public void Markets_StockEveryTradingPost_WithRoutesToOtherIslands()
    {
        var world = new World(Archipelago.Size);
        foreach (var island in Archipelago.CreateIslands())
            world.AddIsland(island);
        Contracts.OpenMarkets(world, seed: 3);

        foreach (var post in world.Islands.Where(i => i.HasShipyard))
        {
            var offers = world.Trade.OffersAt(post.Id);
            Assert.Equal(Contracts.OffersPerPost, offers.Count);
            Assert.All(offers, c => Assert.NotEqual(post.Id, c.DestinationIslandId));
            Assert.Equal(offers.Count, offers.Select(c => c.DestinationIslandId).Distinct().Count());
            Assert.All(offers, c => Assert.True(c.Payout > c.Cost));
        }
        Assert.All(world.Islands.Where(i => !i.HasShipyard), i => Assert.Empty(world.Trade.OffersAt(i.Id)));
    }

    [Fact]
    public void Payout_GrowsWithCapitalCargoAndDistance()
    {
        var baseline = Contracts.PayoutFor(20, 5, 50f);
        Assert.True(Contracts.PayoutFor(40, 5, 50f) > baseline);
        Assert.True(Contracts.PayoutFor(20, 10, 50f) > baseline);
        Assert.True(Contracts.PayoutFor(20, 5, 120f) > baseline);
    }

    [Fact]
    public void Buying_PaysTheCost_FillsTheHold_AndRestocksTheBoard()
    {
        var world = TwoIslands();
        var ship = ShipOf(world, Trader);
        var offer = Offer(world, 1);

        var events = Buy(world, offer.Id);

        Assert.Equal(1000 - offer.Cost, Gold(world));
        Assert.Equal(new CargoLot(offer, offer.CargoUnits), Assert.Single(ship.Cargo));
        Assert.Equal(offer.CargoUnits, ship.CargoUsed);
        Assert.Contains(events, e => e is ContractPurchased p && p.Contract == offer && p.ShipId == ship.Id);

        // A new contract takes its place on the board, in the same slot.
        var board = world.Trade.OffersAt(1);
        Assert.Equal(Contracts.OffersPerPost, board.Count);
        Assert.DoesNotContain(offer, board);
        Assert.Contains(events, e => e is ContractsOffered o && o.IslandId == 1 && o.Offers.SequenceEqual(board));
    }

    [Fact]
    public void Buying_IsRefused_WithoutTheGold()
    {
        var world = TwoIslands(gold: 0);
        var offer = Offer(world);

        var events = Buy(world, offer.Id);

        Assert.Empty(ShipOf(world, Trader).Cargo);
        Assert.Contains(events, e => e is CommandRejected { Reason: RejectionReason.NotEnoughGold });
    }

    [Fact]
    public void Buying_IsRefused_WithoutRoomInTheHold()
    {
        var world = TwoIslands();
        var ship = ShipOf(world, Trader);
        ship.AddModifier(new StatModifier(StatId.CargoCapacity, ModifierKind.Flat, Offer(world).CargoUnits - 1 - ship.CargoCapacity, "test"));

        var events = Buy(world, Offer(world).Id);

        Assert.Empty(ship.Cargo);
        Assert.Equal(1000, Gold(world));
        Assert.Contains(events, e => e is CommandRejected { Reason: RejectionReason.NotEnoughCargoSpace });
    }

    [Fact]
    public void CargoCapacity_IsAStatUpgradesCanRaise()
    {
        var world = TwoIslands();
        var ship = ShipOf(world, Trader);
        var before = ship.CargoCapacity;

        ship.AddModifier(new StatModifier(StatId.CargoCapacity, ModifierKind.Flat, 4, "test"));

        Assert.Equal(before + 4, ship.CargoCapacity);
    }

    [Fact]
    public void Buying_IsRefused_AwayFromTheTradingPost_OrForAContractNotOnOffer()
    {
        var world = TwoIslands();
        var ship = ShipOf(world, Trader);

        Assert.Contains(Buy(world, contractId: 9999), e => e is CommandRejected { Reason: RejectionReason.UnknownContract });

        var offer = Offer(world);
        MoveTo(world, ship, OpenSea);
        Assert.Contains(Buy(world, offer.Id), e => e is CommandRejected { Reason: RejectionReason.NotAtShipyard });
        Assert.Empty(ship.Cargo);
    }

    [Fact]
    public void AnchoringOffTheDestination_DeliversForTheFullPayout()
    {
        var world = TwoIslands();
        var ship = ShipOf(world, Trader);
        var offer = Offer(world);
        Buy(world, offer.Id);
        var afterPurchase = Gold(world);

        world.DrainEvents();
        MoveTo(world, ship, DestinationAnchorage);

        // Delivered for the payout (plundering the island adds its own gold a few seconds later).
        Assert.Contains(world.DrainEvents(), e => e is ContractDelivered d && d.ContractId == offer.Id && d.Payout == offer.Payout);
        Assert.Equal(afterPurchase + offer.Payout, Gold(world));
        Assert.Empty(ship.Cargo);
        Assert.Equal(0, ship.CargoUsed);
    }

    [Fact]
    public void Cargo_IsOnlyDeliveredAtItsOwnDestination()
    {
        var world = TwoIslands();
        var ship = ShipOf(world, Trader);
        var offer = Offer(world);
        Buy(world, offer.Id);

        // Still at the post it came from: not its destination, so nothing pays.
        for (var t = 0; t < 10; t++)
            world.Step();
        MoveTo(world, ship, OpenSea);

        Assert.Single(ship.Cargo);
        Assert.Equal(1000 - offer.Cost, Gold(world));
    }

    [Fact]
    public void Sinking_SpillsTheSurvivingShareAsACrate_AndThePayoutShrinksToMatch()
    {
        var world = TwoIslands();
        var ship = ShipOf(world, Trader);
        var offer = Offer(world);
        Buy(world, offer.Id);
        MoveTo(world, ship, OpenSea);

        Sink(world, ship);

        var crate = Assert.Single(world.Trade.Crates);
        var surviving = (int)Math.Round(offer.CargoUnits * 0.6, MidpointRounding.AwayFromZero);
        Assert.Equal(surviving, crate.Cargo.RemainingUnits);
        Assert.Equal(offer, crate.Cargo.Contract); // original terms, destination, and id all survive
        Assert.Equal(Contracts.RoundGold(offer.Payout * (double)surviving / offer.CargoUnits), crate.Cargo.Payout);
        Assert.True(Vector2.Distance(crate.Position, OpenSea) < 2f);
    }

    [Fact]
    public void PartialPayout_IsTheOriginalScaledByWhatSurvives()
    {
        var contract = new TradeContract(1, 1, 2, Cost: 40, Payout: 100, CargoUnits: 10);

        Assert.Equal(60, new CargoLot(contract, 6).Payout);
        Assert.Equal(30, new CargoLot(contract, 3).Payout);
        Assert.Equal(100, new CargoLot(contract, 10).Payout);
    }

    [Fact]
    public void AnotherShip_CanRecoverTheCrate_AndDeliverItForTheReducedPayout()
    {
        var world = TwoIslands();
        var trader = ShipOf(world, Trader);
        var offer = Offer(world);
        Buy(world, offer.Id);
        MoveTo(world, trader, OpenSea);
        Sink(world, trader);
        var lot = world.Trade.Crates.Single().Cargo;

        var rival = world.SpawnShip(OpenSea + new Vector2(2, 0), 0f, ShipStats.Sloop, Rival, Loadouts.Sloop);
        world.DrainEvents();
        Anchor(world, rival);

        Assert.Empty(world.Trade.Crates);
        Assert.Equal(lot, Assert.Single(rival.Cargo));
        Assert.Equal(lot.RemainingUnits, rival.CargoUsed);
        Assert.Contains(world.DrainEvents(), e => e is CargoRecovered r && r.ShipId == rival.Id && r.PlayerId == Rival);

        MoveTo(world, rival, DestinationAnchorage);
        Assert.Equal(lot.Payout, Gold(world, Rival));
        Assert.Empty(rival.Cargo);
    }

    [Fact]
    public void Recovery_WaitsForRoomInTheHold()
    {
        var world = TwoIslands();
        var trader = ShipOf(world, Trader);
        Buy(world, Offer(world).Id);
        MoveTo(world, trader, OpenSea);
        Sink(world, trader);

        var rival = world.SpawnShip(OpenSea + new Vector2(2, 0), 0f, ShipStats.Sloop, Rival, Loadouts.Sloop);
        rival.AddModifier(new StatModifier(StatId.CargoCapacity, ModifierKind.Flat, -rival.CargoCapacity + 1, "test"));
        Anchor(world, rival);

        Assert.Single(world.Trade.Crates);
        Assert.Empty(rival.Cargo);
    }

    [Fact]
    public void RepeatedSinkings_WearCargoDown_UntilItsGone()
    {
        var world = TwoIslands();
        world.Trade.CargoSurvivalFraction = 0.6f;
        world.Trade.MinSurvivingUnits = 2;
        var contract = new TradeContract(500, 1, 2, Cost: 40, Payout: 100, CargoUnits: 10);
        var carrier = ShipOf(world, Trader);
        carrier.LoadCargo(new CargoLot(contract, 10));

        var seen = new List<int>();
        for (var round = 0; round < 10 && carrier is not null; round++)
        {
            MoveTo(world, carrier, OpenSea);
            world.DrainEvents();
            Sink(world, carrier);
            var events = world.DrainEvents();
            if (world.Trade.Crates.Count == 0)
            {
                Assert.Contains(events, e => e is CargoLost { ContractId: 500 });
                carrier = null;
                break;
            }

            var lot = world.Trade.Crates.Single().Cargo;
            seen.Add(lot.RemainingUnits);
            Assert.Equal(contract, lot.Contract);

            // Someone new picks it up each time.
            var next = world.SpawnShip(OpenSea + new Vector2(1, 0), 0f, ShipStats.Sloop, 10 + round, Loadouts.Sloop);
            Anchor(world, next);
            Assert.Single(next.Cargo);
            carrier = next;
        }

        Assert.Equal(new[] { 6, 4, 2 }, seen); // 10 -> 6 -> 3.6 (4) -> 2.4 (2) -> 1.2: below 2, gone
        Assert.Null(carrier);
        Assert.Empty(world.Trade.Crates);
    }

    [Fact]
    public void SurvivingUnits_AlwaysFall_EvenWithAGenerousSurvivalRate()
    {
        var board = new TradeBoard { CargoSurvivalFraction = 0.99f };

        Assert.Equal(4, Contracts.SurvivingUnits(board, 5));
        Assert.Equal(0, Contracts.SurvivingUnits(board, 1));
    }

    [Fact]
    public void LeavingTheRun_SpillsCargoToo()
    {
        var world = TwoIslands();
        Buy(world, Offer(world).Id);

        world.RemovePlayer(Trader);

        Assert.Single(world.Trade.Crates);
    }

    [Fact]
    public void Markets_AreReproducibleFromTheSeed()
    {
        static IReadOnlyList<TradeContract> Board(int seed)
        {
            var world = new World(Archipelago.Size);
            foreach (var island in Archipelago.CreateIslands())
                world.AddIsland(island);
            Contracts.OpenMarkets(world, seed);
            return world.Trade.Offers.OrderBy(o => o.Key).SelectMany(o => o.Value).ToList();
        }

        Assert.Equal(Board(11), Board(11));
    }
}
