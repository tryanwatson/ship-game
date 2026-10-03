namespace ShipGame.Shared.Progression;

/// <summary>Per-player run state that belongs to the player rather than their ship.</summary>
public sealed class PlayerState
{
    public PlayerState(int playerId)
    {
        PlayerId = playerId;
    }

    public int PlayerId { get; }

    /// <summary>Change through <see cref="Simulation.World.AddGold"/> in game code, so the change is announced.</summary>
    public int Gold { get; set; }

    public int Kills { get; set; }

    /// <summary>Ticks until this player's ship comes back; 0 while afloat (or once the run is over).</summary>
    public int RespawnTicksRemaining { get; set; }

    public bool IsAwaitingRespawn => RespawnTicksRemaining > 0;

    /// <summary>The ship that went down, kept so the replacement can inherit its hull, guns, and upgrades. Server-side only.</summary>
    public Simulation.Ship? LostShip { get; set; }
}
