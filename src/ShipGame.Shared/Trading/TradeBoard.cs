using System.Numerics;

namespace ShipGame.Shared.Trading;

/// <summary>
/// A world's trade state: the contracts each trading post has on offer, and cargo floating in the sea. The rules
/// that change it live in <see cref="Contracts"/>; a client mirroring the server sets it directly.
/// </summary>
public sealed class TradeBoard
{
    private readonly Dictionary<int, IReadOnlyList<TradeContract>> _offers = new();
    private readonly List<CargoCrate> _crates = new();
    private int _nextContractId = 1;
    private int _nextCrateId = 1;

    /// <summary>Draws new contracts; reseeded when the markets open so a run is reproducible from its seed.</summary>
    internal Random Rng { get; set; } = new(0);

    /// <summary>Share of a lot that survives each time its carrier sinks (rounded to whole units).</summary>
    public float CargoSurvivalFraction { get; set; } = Contracts.DefaultCargoSurvivalFraction;

    /// <summary>A sinking that leaves fewer units than this destroys the lot outright instead of setting it afloat.</summary>
    public int MinSurvivingUnits { get; set; } = Contracts.DefaultMinSurvivingUnits;

    public IReadOnlyDictionary<int, IReadOnlyList<TradeContract>> Offers => _offers;

    public IReadOnlyList<CargoCrate> Crates => _crates;

    /// <summary>The contracts on offer at a trading post; empty for any other island.</summary>
    public IReadOnlyList<TradeContract> OffersAt(int islandId) =>
        _offers.TryGetValue(islandId, out var offers) ? offers : Array.Empty<TradeContract>();

    public void SetOffers(int islandId, IReadOnlyList<TradeContract> offers) => _offers[islandId] = offers.ToArray();

    internal int NextContractId() => _nextContractId++;

    /// <summary>Sets cargo afloat. Explicit ids are for clients mirroring the server, which hands out the real ones.</summary>
    public CargoCrate AddCrate(Vector2 position, CargoLot cargo, int? id = null)
    {
        var crateId = id ?? _nextCrateId++;
        _nextCrateId = Math.Max(_nextCrateId, crateId + 1);
        var crate = new CargoCrate(crateId, position, cargo);
        _crates.Add(crate);
        return crate;
    }

    public CargoCrate? FindCrate(int id) => _crates.Find(c => c.Id == id);

    public bool RemoveCrate(int id) => _crates.RemoveAll(c => c.Id == id) > 0;
}
