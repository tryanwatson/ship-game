using System.Numerics;

namespace ShipGame.Shared.Trading;

/// <summary>
/// A cargo run offered at a trading post: pay <paramref name="Cost"/> up front, carry <paramref name="CargoUnits"/>
/// of cargo to <paramref name="DestinationIslandId"/>, and collect <paramref name="Payout"/> there. These are the
/// contract's original terms; they never change, whoever ends up carrying the cargo (see <see cref="CargoLot"/>).
/// </summary>
public sealed record TradeContract(int Id, int OriginIslandId, int DestinationIslandId, int Cost, int Payout, int CargoUnits);

/// <summary>
/// The cargo for one contract, wherever it is now: in a hold or floating in the sea. Losing cargo only shrinks
/// <paramref name="RemainingUnits"/>; the contract and its destination survive every change of hands.
/// </summary>
public sealed record CargoLot(TradeContract Contract, int RemainingUnits)
{
    /// <summary>What delivering it pays now: the original payout scaled by the share of cargo that's left.</summary>
    public int Payout => Contracts.PayoutFor(Contract, RemainingUnits);
}

/// <summary>Cargo floating where a ship went down, free for any ship that anchors beside it.</summary>
public sealed record CargoCrate(int Id, Vector2 Position, CargoLot Cargo);
