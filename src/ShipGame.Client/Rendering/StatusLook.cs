using Microsoft.Xna.Framework;
using ShipGame.Shared.Simulation;

namespace ShipGame.Client.Rendering;

/// <summary>How each status looks wherever it's shown (the pips over a ship, the row over the ability bar).</summary>
public static class StatusLook
{
    /// <summary>The Hunter's Mark red, shared with the target mark over a marked ship.</summary>
    public static readonly Color MarkColor = new(255, 90, 70);

    public static Color Color(StatusId id) => id switch
    {
        StatusId.Burning => new Color(255, 140, 40),
        StatusId.Frenzy => new Color(235, 60, 70),
        StatusId.Marked => MarkColor,
        StatusId.Slowed => new Color(100, 160, 240),
        StatusId.Entrenched => new Color(220, 190, 110),
        _ => new Color(250, 250, 250),
    };

    /// <summary>The emblem of the card that brings it on, so the two read as one.</summary>
    public static CardIcon Icon(StatusId id) => id switch
    {
        StatusId.Burning => CardIcon.Fire,
        StatusId.Frenzy => CardIcon.Reload,
        StatusId.Marked => CardIcon.Range,
        StatusId.Slowed => CardIcon.Chain,
        StatusId.Entrenched => CardIcon.Anchor,
        _ => CardIcon.Damage,
    };
}
