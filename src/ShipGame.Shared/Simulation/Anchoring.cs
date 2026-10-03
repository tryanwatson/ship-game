namespace ShipGame.Shared.Simulation;

public enum AnchorState
{
    /// <summary>Anchor up: free to sail.</summary>
    Weighed,

    /// <summary>Anchor down: the ship holds station, can't move or turn.</summary>
    Down,

    /// <summary>Hauling the anchor in: still held fast until it's up.</summary>
    Raising,
}

/// <summary>
/// Anchor handling. Players let go by holding the anchor key for <see cref="DropSeconds"/> (the ship sails on
/// meanwhile), and weighing takes <see cref="RaiseSeconds"/>. The hold is timed here, not on the client, so nobody
/// can anchor instantly.
/// </summary>
public static class Anchoring
{
    public const float DropSeconds = 1f;
    public static readonly int DropTicks = (int)(DropSeconds * SimConstants.TickRate);

    public const float RaiseSeconds = 3f;
    public static readonly int RaiseTicks = (int)(RaiseSeconds * SimConstants.TickRate);

    /// <summary>The anchor key went down: start letting go if the anchor's up, start hauling it in if it's down.</summary>
    public static void PressKey(Ship ship)
    {
        switch (ship.Anchor)
        {
            case AnchorState.Weighed when ship.AnchorDropTicksRemaining == 0:
                ship.AnchorDropTicksRemaining = DropTicks;
                break;
            case AnchorState.Down:
                ship.Anchor = AnchorState.Raising;
                ship.AnchorRaiseTicksRemaining = RaiseTicks;
                break;
        }
    }

    /// <summary>The anchor key came up: a drop that hasn't happened yet is called off.</summary>
    public static void ReleaseKey(Ship ship) => ship.AnchorDropTicksRemaining = 0;

    /// <summary>Lets go the anchor: the ship is brought up short where it is.</summary>
    public static void Drop(Ship ship)
    {
        ship.Anchor = AnchorState.Down;
        ship.AnchorRaiseTicksRemaining = 0;
        ship.AnchorDropTicksRemaining = 0;
        ship.Speed = 0f;
        ship.WindDrift = default;
        ship.MoveTarget = null;
        ship.IsHoldingCourse = false;
    }

    /// <summary>Instant weigh, for NPCs (and setup) that skip the capstan.</summary>
    public static void Weigh(Ship ship)
    {
        ship.Anchor = AnchorState.Weighed;
        ship.AnchorRaiseTicksRemaining = 0;
        ship.AnchorDropTicksRemaining = 0;
    }

    public static void Tick(Ship ship)
    {
        if (ship.AnchorDropTicksRemaining > 0 && --ship.AnchorDropTicksRemaining == 0)
            Drop(ship);
        if (ship.Anchor == AnchorState.Raising && --ship.AnchorRaiseTicksRemaining <= 0)
            Weigh(ship);
    }

    /// <summary>0..1 while the key is held to let go; 0 otherwise.</summary>
    public static float DropProgress(Ship ship) =>
        ship.AnchorDropTicksRemaining > 0 ? 1f - (float)ship.AnchorDropTicksRemaining / DropTicks : 0f;

    /// <summary>0 when the haul starts, 1 when the anchor is up.</summary>
    public static float RaiseProgress(Ship ship) =>
        ship.Anchor == AnchorState.Raising ? 1f - (float)ship.AnchorRaiseTicksRemaining / RaiseTicks : 0f;
}
