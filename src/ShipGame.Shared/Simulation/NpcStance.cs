namespace ShipGame.Shared.Simulation;

/// <summary>
/// What an NPC is up to, as far as anyone watching can tell. Behaviors set it; renderers (and, over the network,
/// clients that don't run the AI) read it instead of reaching into the behavior.
/// </summary>
public enum NpcStance
{
    /// <summary>Not an NPC, or one with nothing to show.</summary>
    None,

    /// <summary>Waiting at its post; will engage anything that comes close.</summary>
    Guarding,

    /// <summary>After someone.</summary>
    Hunting,

    /// <summary>Gave up the chase and heading home.</summary>
    Returning,
}
