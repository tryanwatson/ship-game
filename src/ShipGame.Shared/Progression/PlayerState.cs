namespace ShipGame.Shared.Progression;

/// <summary>Per-player run state that belongs to the player rather than their ship.</summary>
public sealed class PlayerState
{
    public PlayerState(int playerId)
    {
        PlayerId = playerId;
    }

    public int PlayerId { get; }

    /// <summary>What they're called, over their ship and on the map (see <see cref="PlayerNames"/>).</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// At the start of a run, until they've chosen the weapon they set sail with (after their starting card). The game
    /// waits for it like it waits for cards.
    /// </summary>
    public bool NeedsStartingWeapon { get; set; }

    /// <summary>Change through <see cref="Simulation.World.AddGold"/> in game code, so the change is announced.</summary>
    public int Gold { get; set; }

    public int Kills { get; set; }

    /// <summary>Ticks until this player's ship comes back; 0 while afloat (or once the run is over).</summary>
    public int RespawnTicksRemaining { get; set; }

    public bool IsAwaitingRespawn => RespawnTicksRemaining > 0;

    /// <summary>The ship that went down, kept so the replacement can inherit its hull, guns, skills, and upgrades. Server-side only.</summary>
    public Simulation.Ship? LostShip { get; set; }

    /// <summary>
    /// Cards waiting to be chosen from, oldest first: one offer for each fortress taken or boss sunk (and the starting card).
    /// Change through <see cref="CardRewards"/> in game code, so changes are announced.
    /// </summary>
    public List<CardOffer> CardOffers { get; } = new();

    /// <summary>Times this player has rerolled an offer this run: each one doubles the price of the next (see <see cref="CardRewards.RerollCost"/>).</summary>
    public int Rerolls { get; set; }

    /// <summary>Every card this player has chosen. They belong to the player: a ship that sinks hands them on to the next.</summary>
    public List<Upgrades.CardPick> Cards { get; } = new();
}
