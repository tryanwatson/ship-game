using System.Numerics;

namespace ShipGame.Shared.Simulation;

/// <summary>
/// What a shot does beyond plain damage, set by the ability that fired it from its configuration (see
/// <see cref="Abilities.AbilityStat"/>). Server-side only: clients just fly the ball.
/// </summary>
public sealed record ShotEffects
{
    public static readonly ShotEffects None = new();

    /// <summary>The ability that fired it, for cooldown refunds.</summary>
    public string? AbilityId { get; init; }

    /// <summary>Ships it passes through before stopping.</summary>
    public int Pierce { get; init; }

    /// <summary>Hits within this distance of the muzzle deal <see cref="CloseDamageBonus"/> extra.</summary>
    public float CloseRange { get; init; }

    public float CloseDamageBonus { get; init; }

    /// <summary>Hits at least this far from the muzzle deal <see cref="LongDamageBonus"/> extra and refund <see cref="LongRangeRefund"/>.</summary>
    public float LongRange { get; init; } = float.PositiveInfinity;

    public float LongDamageBonus { get; init; }

    /// <summary>Fraction of the firing ability's cooldown a long-range hit gives back.</summary>
    public float LongRangeRefund { get; init; }

    /// <summary>Fraction a hit slows the ship it strikes, for <see cref="World.SlowSeconds"/> (Chain Shot).</summary>
    public float SlowOnHit { get; init; }

    /// <summary>Flies over land (Railgun).</summary>
    public bool IgnoresLand { get; init; }

    /// <summary>Times a hit bounces on to the next enemy nearby (Ricochet).</summary>
    public int Ricochets { get; init; }

    public bool IsLongRange(float distance) => distance >= LongRange;

    /// <summary>Damage multiplier for a hit <paramref name="distance"/> from the muzzle.</summary>
    public float DamageMultiplier(float distance) =>
        1f + (distance <= CloseRange ? CloseDamageBonus : 0f) + (IsLongRange(distance) ? LongDamageBonus : 0f);
}

public sealed class Projectile
{
    /// <summary>Size of an ordinary cannonball.</summary>
    public const float DefaultRadius = 0.15f;

    public Projectile(int id, int ownerShipId, Team team, float damage, float radius = DefaultRadius)
    {
        Id = id;
        Radius = radius;
        OwnerShipId = ownerShipId;
        Team = team;
        Damage = damage;
    }

    public int Id { get; }

    /// <summary>How close to a hull (or shore) the shot has to pass to hit it.</summary>
    public float Radius { get; }

    /// <summary>The ship that fired it; projectiles never hit their own ship.</summary>
    public int OwnerShipId { get; }

    /// <summary>The firing ship's team; projectiles pass harmlessly through friendly ships.</summary>
    public Team Team { get; }

    public float Damage { get; }

    public Vector2 Position { get; set; }

    public Vector2 PreviousPosition { get; set; }

    public Vector2 Velocity { get; set; }

    public int RemainingTicks { get; set; }

    /// <summary>Where it was fired from, for range-dependent effects.</summary>
    public Vector2 Origin { get; init; }

    public ShotEffects Effects { get; init; } = ShotEffects.None;

    /// <summary>
    /// Ships it strikes are tested where they were this many ticks ago: where the player who fired it saw them (see
    /// <see cref="Ship.ShotRewindTicks"/>).
    /// </summary>
    public int RewindTicks { get; init; }

    /// <summary>A fort's shot flies out over the island the fort stands on: land there doesn't stop it.</summary>
    public int? IgnoredIslandId { get; init; }

    /// <summary>Ships it can still pass through; starts at <see cref="ShotEffects.Pierce"/>.</summary>
    public int PierceRemaining { get; set; }

    private List<int>? _shipsHit;

    /// <summary>Whether it already struck this ship (a piercing shot hits each ship once).</summary>
    public bool HasHit(int shipId) => _shipsHit?.Contains(shipId) == true;

    public void RecordHit(int shipId) => (_shipsHit ??= new List<int>()).Add(shipId);
}
