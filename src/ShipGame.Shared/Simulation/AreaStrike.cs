using System.Numerics;

namespace ShipGame.Shared.Simulation;

/// <summary>A shell in the air: lands on <see cref="Target"/> at <see cref="ImpactTick"/> and hurts every hostile ship in <see cref="Radius"/>.</summary>
public sealed class AreaStrike
{
    public required int Id { get; init; }
    public required int OwnerShipId { get; init; }
    public required Team Team { get; init; }
    public required Vector2 Origin { get; init; }
    public required Vector2 Target { get; init; }
    public required float Radius { get; init; }
    public required float Damage { get; init; }
    public required long LaunchTick { get; init; }
    public required long ImpactTick { get; init; }

    /// <summary>0 at launch, 1 at impact.</summary>
    public float Progress(double tick) =>
        ImpactTick <= LaunchTick ? 1f : (float)Math.Clamp((tick - LaunchTick) / (ImpactTick - LaunchTick), 0, 1);
}
