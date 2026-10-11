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

    /// <summary>Times sunk at the current stop on the chart: each one keeps them out longer (see <see cref="Respawning.DelayTicksFor"/>).</summary>
    public int DeathsThisStop { get; set; }

    /// <summary>
    /// A lone sailor's lifeboats left this act: sinking with one to spare puts them back in the fight instead of
    /// ending the run (see <see cref="Respawning"/>). Crews have each other instead.
    /// </summary>
    public int ExtraLives { get; set; }

    /// <summary>Sunk, and coming back on a lifeboat (<see cref="ExtraLives"/>): the run isn't over while they wait. Server-side only.</summary>
    public bool OnLifeboat { get; set; }

    /// <summary>Card packs bought at the port the crew is at: each one doubles the price of the next (see <see cref="Upgrades.Shipyards.CardPackCost"/>).</summary>
    public int PacksBoughtHere { get; set; }

    /// <summary>The ship that went down, kept so the replacement can inherit its hull, guns, skills, and upgrades. Server-side only.</summary>
    public Simulation.Ship? LostShip { get; set; }

    /// <summary>
    /// Cards waiting to be chosen from, oldest first: one offer for each fortress taken or boss sunk (and the starting card).
    /// Change through <see cref="CardRewards"/> in game code, so changes are announced.
    /// </summary>
    public List<CardOffer> CardOffers { get; } = new();

    /// <summary>Times this player has rerolled an offer this run: each one doubles the price of the next (see <see cref="CardRewards.RerollCost"/>).</summary>
    public int Rerolls { get; set; }

    /// <summary>The chart stop this player has voted to sail to next, if they've voted (see <see cref="RunDirector"/>).</summary>
    public int? CourseVote { get; set; }

    /// <summary>Every card this player has chosen. They belong to the player: a ship that sinks hands them on to the next.</summary>
    public List<Upgrades.CardPick> Cards { get; } = new();
}
