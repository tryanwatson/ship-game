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
}
