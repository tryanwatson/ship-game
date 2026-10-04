using System.Numerics;
using ShipGame.Shared.Abilities;

namespace ShipGame.Shared.Simulation;

/// <summary>
/// A shot that's been laid but not yet fired (see <see cref="Ability.WindupTicksFor"/>): at <see cref="FireTick"/> the
/// ship's ability on <see cref="Slot"/> fires at <see cref="Target"/> (see <see cref="Ability.CastWarned"/>), from wherever the ship is by then. Public,
/// like a shell's landing spot, so the ship it's aimed at can get out of the way. Dropped if the ship sinks first.
/// </summary>
public sealed class ShotWarning
{
    public required int ShipId { get; init; }
    public required AbilitySlot Slot { get; init; }

    /// <summary>The cooldown channel it was laid with: for a broadside, the side that fires, however the ship turns meanwhile.</summary>
    public int Channel { get; init; }
    public required Vector2 Target { get; init; }
    public required long StartTick { get; init; }
    public required long FireTick { get; init; }

    /// <summary>0 when laid, 1 as it fires.</summary>
    public float Progress(double tick) =>
        FireTick <= StartTick ? 1f : (float)Math.Clamp((tick - StartTick) / (FireTick - StartTick), 0, 1);
}
