using System.Numerics;

namespace ShipGame.Shared.Simulation;

/// <summary>Smaller blasts a shell throws out around its impact: <paramref name="Count"/> of them, evenly round a ring.</summary>
/// <param name="DamageFraction">Each bomblet's damage, as a fraction of the shell's.</param>
/// <param name="DelayTicks">How long after the main burst the bomblets go off.</param>
public sealed record ClusterEffect(int Count, float Spread, float Radius, float DamageFraction, int DelayTicks);

/// <summary>A patch of water a shell sets burning (Firestorm): <paramref name="Dps"/> to hostile ships in it, for <paramref name="Ticks"/>.</summary>
public sealed record FireEffect(float Dps, int Ticks);

/// <summary>Burning water left by a shell: hurts hostile ships within <see cref="Radius"/> every tick until <see cref="EndTick"/>.</summary>
public sealed class FireZone
{
    public required int Id { get; init; }
    public required int OwnerShipId { get; init; }
    public required Team Team { get; init; }
    public required Vector2 Position { get; init; }
    public required float Radius { get; init; }
    public required float Dps { get; init; }
    public required long StartTick { get; init; }
    public required long EndTick { get; init; }
}

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

    /// <summary>Bomblets to scatter on impact. Server-side only: clients see them as strikes of their own.</summary>
    public ClusterEffect? Cluster { get; init; }

    /// <summary>Fire to leave where it lands. Server-side only: clients hear of the fire when it starts.</summary>
    public FireEffect? Fire { get; init; }

    /// <summary>0 at launch, 1 at impact.</summary>
    public float Progress(double tick) =>
        ImpactTick <= LaunchTick ? 1f : (float)Math.Clamp((tick - LaunchTick) / (ImpactTick - LaunchTick), 0, 1);
}
