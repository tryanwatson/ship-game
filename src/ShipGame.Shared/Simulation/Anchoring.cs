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

/// <summary>Anchor handling: dropping is instant, weighing takes <see cref="RaiseSeconds"/>.</summary>
public static class Anchoring
{
    public const float RaiseSeconds = 10f;
    public static readonly int RaiseTicks = (int)(RaiseSeconds * SimConstants.TickRate);

    /// <summary>The X key: drop the anchor if it's up, start hauling it in if it's down. Ignored mid-raise.</summary>
    public static void Toggle(Ship ship)
    {
        switch (ship.Anchor)
        {
            case AnchorState.Weighed:
                Drop(ship);
                break;
            case AnchorState.Down:
                ship.Anchor = AnchorState.Raising;
                ship.AnchorRaiseTicksRemaining = RaiseTicks;
                break;
        }
    }

    /// <summary>Lets go the anchor: the ship is brought up short where it is.</summary>
    public static void Drop(Ship ship)
    {
        ship.Anchor = AnchorState.Down;
        ship.AnchorRaiseTicksRemaining = 0;
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
    }

    public static void Tick(Ship ship)
    {
        if (ship.Anchor != AnchorState.Raising)
            return;
        if (--ship.AnchorRaiseTicksRemaining <= 0)
            Weigh(ship);
    }

    /// <summary>0 when the haul starts, 1 when the anchor is up.</summary>
    public static float RaiseProgress(Ship ship) =>
        ship.Anchor == AnchorState.Raising ? 1f - (float)ship.AnchorRaiseTicksRemaining / RaiseTicks : 0f;
}
