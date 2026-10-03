using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;

namespace ShipGame.Shared.Simulation;

/// <summary>
/// Something that happened in the world, for whoever needs to hear about it: the server forwards these to
/// clients (reliably), and the client can drive effects and sounds from them. Continuous state (positions,
/// health, anchor state) travels in snapshots instead; events cover discrete, one-off changes.
/// </summary>
public abstract record WorldEvent(long Tick);

public sealed record ShipSpawned(long Tick, int ShipId) : WorldEvent(Tick);

/// <param name="KillerShipId">Ship that landed the last hit, if any (it may have sunk too).</param>
public sealed record ShipSunk(long Tick, int ShipId, int? KillerShipId) : WorldEvent(Tick);

/// <summary>Everything a client needs to fly the ball itself: it moves in a straight line until it expires or the server reports an impact.</summary>
public sealed record ProjectileSpawned(
    long Tick, int ProjectileId, int OwnerShipId, Team Team, Vector2 Position, Vector2 Velocity, float Damage, int LifetimeTicks,
    float Radius = Projectile.DefaultRadius)
    : WorldEvent(Tick);

/// <summary>A shell is in the air: everyone can see where (<paramref name="Target"/>) and when (<paramref name="ImpactTick"/>) it lands.</summary>
public sealed record AreaStrikeLaunched(
    long Tick, int StrikeId, int OwnerShipId, Team Team, Vector2 Origin, Vector2 Target, float Radius, float Damage, long ImpactTick)
    : WorldEvent(Tick);

/// <summary>Map cells a team has just discovered (see <see cref="Discovery"/>).</summary>
public sealed record AreaDiscovered(long Tick, Team Team, IReadOnlyList<int> Cells) : WorldEvent(Tick)
{
    // Records compare lists by reference; compare the cells themselves.
    public bool Equals(AreaDiscovered? other) =>
        other is not null && Tick == other.Tick && Team == other.Team && Cells.SequenceEqual(other.Cells);

    public override int GetHashCode() => HashCode.Combine(Tick, Team, Cells.Count);
}

/// <summary>A shell burst. Damage arrives via ship health; this is for the explosion.</summary>
public sealed record AreaStrikeImpact(long Tick, int StrikeId, Vector2 Target, float Radius) : WorldEvent(Tick);

/// <param name="ShipId">The ship it struck, or null if it hit land.</param>
public sealed record ProjectileImpact(long Tick, int ProjectileId, int? ShipId) : WorldEvent(Tick);

/// <param name="Channel">Which of the ability's cooldowns it used (the broadside's side; 0 for most).</param>
public sealed record AbilityCast(long Tick, int ShipId, AbilitySlot Slot, int CooldownTicks, int Channel = 0) : WorldEvent(Tick);

public sealed record ShipGrounded(long Tick, int ShipId) : WorldEvent(Tick);

public sealed record GoldChanged(long Tick, int PlayerId, int Gold, int Delta) : WorldEvent(Tick);

public sealed record IslandPlundered(long Tick, int IslandId, int PlayerId, int Gold, int CooldownTicks) : WorldEvent(Tick);

public sealed record UpgradePurchased(long Tick, int ShipId, string UpgradeId, int Level) : WorldEvent(Tick);

public sealed record WaveStarted(long Tick, int Wave, int Pirates) : WorldEvent(Tick);

/// <summary>A player's ship went down; they'll be back in <paramref name="RespawnTicks"/> unless the run ends first.</summary>
public sealed record PlayerSunk(long Tick, int PlayerId, int RespawnTicks) : WorldEvent(Tick);

public sealed record PlayerRespawned(long Tick, int PlayerId, int ShipId) : WorldEvent(Tick);

/// <summary>Every player was sunk at once: game over for this run.</summary>
public sealed record RunEnded(long Tick) : WorldEvent(Tick);

/// <summary>A player's command was refused; the server tells that player why.</summary>
public sealed record CommandRejected(long Tick, int PlayerId, Command Command, RejectionReason Reason) : WorldEvent(Tick);

public enum RejectionReason
{
    /// <summary>The player has no ship afloat.</summary>
    NoShip,

    /// <summary>Can't sail while the anchor is down or being raised.</summary>
    Anchored,

    /// <summary>The anchor is already being raised.</summary>
    AnchorBusy,

    InvalidSlot,
    EmptySlot,
    OnCooldown,
    CastFailed,

    NotAtShipyard,
    IslandOnCooldown,
    UnknownUpgrade,
    MaxLevel,
    NotEnoughGold,
}
