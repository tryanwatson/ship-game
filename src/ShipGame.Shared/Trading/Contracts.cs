using System.Numerics;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Trading;

/// <summary>
/// Cargo contracts. Shipyards double as trading posts, each offering <see cref="OffersPerPost"/> contracts to other
/// islands; buying one costs gold and fills part of the hold, and anchoring off the destination delivers it. A ship
/// that sinks spills its cargo: <see cref="TradeBoard.CargoSurvivalFraction"/> of it floats as a crate anyone can
/// anchor beside to take over the delivery, and the payout shrinks with every unit lost.
/// </summary>
public static class Contracts
{
    public const int OffersPerPost = 3;

    // Contract terms are drawn from these ranges.
    public const int MinCost = 10;
    public const int MaxCost = 40;
    public const int CostStep = 5;
    public const int MinCargoUnits = 3;
    public const int MaxCargoUnits = 10;

    /// <summary>Every contract returns its cost plus this share of it, before any pay for hauling.</summary>
    public const float CapitalReturn = 0.25f;

    /// <summary>Gold per unit of cargo per tile between the two islands.</summary>
    public const float PayPerUnitTile = 0.04f;

    /// <summary>How close (tiles from ship center to shore) a ship must anchor off the destination to deliver.</summary>
    public const float DeliveryRange = Plundering.Range;

    /// <summary>How close (tiles from ship center) a ship must anchor to a crate to haul it aboard.</summary>
    public const float RecoveryRange = 3f;

    /// <summary>Crates from one wreck float this far apart, so each can be seen.</summary>
    private const float CrateSpread = 1.2f;

    public const float DefaultCargoSurvivalFraction = 0.6f;
    public const int DefaultMinSurvivingUnits = 2;

    /// <summary>Gold rounds to the nearest whole coin, halves away from zero.</summary>
    public static int RoundGold(double gold) => (int)Math.Round(gold, MidpointRounding.AwayFromZero);

    /// <summary>Distance a contract is paid for: straight between the two islands' centers.</summary>
    public static float RouteDistance(Island from, Island to) => Vector2.Distance(from.Center, to.Center);

    /// <summary>The original payout for a contract on these terms: more capital, more cargo, and more distance all pay more.</summary>
    public static int PayoutFor(int cost, int cargoUnits, float distance) =>
        RoundGold(cost * (1.0 + CapitalReturn) + cargoUnits * distance * PayPerUnitTile);

    /// <summary>Original payout × (remaining cargo / original cargo).</summary>
    public static int PayoutFor(TradeContract contract, int remainingUnits) =>
        contract.CargoUnits <= 0 ? 0 : RoundGold(contract.Payout * (double)remainingUnits / contract.CargoUnits);

    /// <summary>
    /// Units of <paramref name="units"/> that survive a sinking. Always at least one fewer, so even a generous
    /// survival rate wears cargo down and it eventually drops below <see cref="TradeBoard.MinSurvivingUnits"/>.
    /// </summary>
    public static int SurvivingUnits(TradeBoard board, int units) =>
        Math.Clamp((int)Math.Round(units * (double)board.CargoSurvivalFraction, MidpointRounding.AwayFromZero), 0, Math.Max(0, units - 1));

    /// <summary>Stocks every trading post for a new run, drawing contracts from <paramref name="seed"/>.</summary>
    public static void OpenMarkets(World world, int seed)
    {
        world.Trade.Rng = new Random(seed);
        foreach (var post in world.Islands.Where(i => i.HasShipyard))
        {
            var offers = new List<TradeContract>();
            for (var i = 0; i < OffersPerPost; i++)
            {
                if (CreateContract(world, post, offers) is { } contract)
                    offers.Add(contract);
            }
            Publish(world, post.Id, offers);
        }
    }

    /// <summary>
    /// A fresh contract from <paramref name="origin"/> to some other island, preferring destinations the post isn't
    /// already offering so each one on the board is a different route. Null if there's nowhere to go.
    /// </summary>
    private static TradeContract? CreateContract(World world, Island origin, IReadOnlyList<TradeContract> alongside)
    {
        var rng = world.Trade.Rng;
        var destinations = world.Islands.Where(i => i.Id != origin.Id).ToList();
        if (destinations.Count == 0)
            return null;
        var unused = destinations.Where(i => alongside.All(c => c.DestinationIslandId != i.Id)).ToList();
        var pool = unused.Count > 0 ? unused : destinations;
        var destination = pool[rng.Next(pool.Count)];

        var cost = MinCost + CostStep * rng.Next((MaxCost - MinCost) / CostStep + 1);
        var units = rng.Next(MinCargoUnits, MaxCargoUnits + 1);
        var payout = PayoutFor(cost, units, RouteDistance(origin, destination));
        return new TradeContract(world.Trade.NextContractId(), origin.Id, destination.Id, cost, payout, units);
    }

    private static void Publish(World world, int islandId, IReadOnlyList<TradeContract> offers)
    {
        world.Trade.SetOffers(islandId, offers);
        world.Emit(new ContractsOffered(world.Tick, islandId, world.Trade.OffersAt(islandId)));
    }

    /// <summary>
    /// Buys a contract on offer at the trading post the ship is anchored at: pays its cost, loads its cargo, and puts
    /// a fresh contract up in its place. Null on success.
    /// </summary>
    public static RejectionReason? TryPurchase(World world, Ship ship, int contractId)
    {
        if (ship.OwnerPlayerId is not { } playerId || Shipyards.DockedAt(world, ship) is not { } post)
            return RejectionReason.NotAtShipyard;

        var offers = world.Trade.OffersAt(post.Id);
        var index = offers.ToList().FindIndex(c => c.Id == contractId);
        if (index < 0)
            return RejectionReason.UnknownContract;
        var contract = offers[index];

        if (world.GetOrAddPlayer(playerId).Gold < contract.Cost)
            return RejectionReason.NotEnoughGold;
        if (ship.FreeCargo < contract.CargoUnits)
            return RejectionReason.NotEnoughCargoSpace;

        world.AddGold(playerId, -contract.Cost);
        ship.LoadCargo(new CargoLot(contract, contract.CargoUnits));
        world.Emit(new ContractPurchased(world.Tick, ship.Id, playerId, contract));

        var restocked = offers.Where(c => c.Id != contractId).ToList();
        if (CreateContract(world, post, restocked) is { } replacement)
            restocked.Insert(index, replacement);
        Publish(world, post.Id, restocked);
        return null;
    }

    /// <summary>Ships riding at anchor haul in any crate beside them that fits, and deliver anything bound here.</summary>
    public static void Step(World world)
    {
        foreach (var ship in world.Ships)
        {
            if (ship.OwnerPlayerId is not { } playerId || ship.Anchor != AnchorState.Down)
                continue;

            foreach (var crate in world.Trade.Crates.ToList())
            {
                if (Vector2.Distance(ship.Position, crate.Position) > RecoveryRange || ship.FreeCargo < crate.Cargo.RemainingUnits)
                    continue;
                world.Trade.RemoveCrate(crate.Id);
                ship.LoadCargo(crate.Cargo);
                world.Emit(new CargoRecovered(world.Tick, crate.Id, ship.Id, playerId));
            }

            foreach (var lot in ship.Cargo.ToList())
            {
                // Checked against the contract's own destination, never anything the client claims.
                var destination = world.FindIsland(lot.Contract.DestinationIslandId);
                if (destination is null || destination.DistanceTo(ship.Position) > DeliveryRange)
                    continue;
                var payout = lot.Payout;
                ship.UnloadCargo(lot.Contract.Id);
                world.AddGold(playerId, payout);
                world.Emit(new ContractDelivered(world.Tick, ship.Id, playerId, lot.Contract.Id, payout));
            }
        }
    }

    /// <summary>
    /// A ship carrying cargo went down: each lot loses part of its cargo, and what's left floats free at the wreck,
    /// unless too little survives, in which case it's gone for good.
    /// </summary>
    public static void SpillCargo(World world, Ship ship)
    {
        var board = world.Trade;
        for (var i = 0; i < ship.Cargo.Count; i++)
        {
            var lot = ship.Cargo[i];
            var surviving = SurvivingUnits(board, lot.RemainingUnits);
            if (surviving < board.MinSurvivingUnits)
            {
                world.Emit(new CargoLost(world.Tick, lot.Contract.Id));
                continue;
            }

            // A ring around the wreck, starting astern.
            var angle = ship.Heading + MathF.PI + MathF.Tau * i / ship.Cargo.Count;
            var offset = ship.Cargo.Count == 1 ? Vector2.Zero : new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * CrateSpread;
            var crate = board.AddCrate(ship.Position + offset, lot with { RemainingUnits = surviving });
            world.Emit(new CargoDropped(world.Tick, crate.Id, crate.Position, crate.Cargo));
        }
        ship.ClearCargo();
    }
}
