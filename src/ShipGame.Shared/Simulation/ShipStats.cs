namespace ShipGame.Shared.Simulation;

/// <summary>
/// Tunable handling for a hull type. Distances are in world units (1 unit = 1 map tile)
/// and speeds in units per second.
/// </summary>
/// <param name="CoastTimeConstant">
/// Seconds for water drag to bleed off most of the ship's way once sail is reduced. Higher drifts further.
/// </param>
/// <param name="MinDeceleration">Constant drag that finally brings a drifting ship to rest instead of creeping forever.</param>
/// <param name="CooldownSpeed">How fast ability cooldowns tick down; 1 is normal, 1.25 recovers 25% faster.</param>
/// <param name="WeaponDamage">Multiplier on weapon damage; 1 is a weapon's base damage.</param>
/// <param name="ProjectileSpeed">Multiplier on projectile speed.</param>
/// <param name="WeaponRange">Multiplier on weapon range.</param>
/// <param name="MinTurnRadius">Tightest turning radius, approached as the ship slows to a crawl.</param>
/// <param name="TurnRadiusAtMaxSpeed">Turning radius at full sail. Radius scales linearly with speed in between.</param>
public readonly record struct ShipStats(
    float MaxSpeed,
    float Acceleration,
    float CoastTimeConstant,
    float MinDeceleration,
    float MinTurnRadius,
    float TurnRadiusAtMaxSpeed,
    float Radius,
    float Length,
    float Beam,
    float MaxHealth,
    float CooldownSpeed = 1f,
    float WeaponDamage = 1f,
    float ProjectileSpeed = 1f,
    float WeaponRange = 1f)
{
    public static readonly ShipStats Sloop = new(
        MaxSpeed: 5f,
        Acceleration: 2f,
        CoastTimeConstant: 1.5f,
        MinDeceleration: 0.2f,
        MinTurnRadius: 1.5f,
        TurnRadiusAtMaxSpeed: 4f,
        Radius: 1f,
        Length: 2.4f,
        Beam: 0.9f,
        MaxHealth: 100f);

    /// <summary>Slowing from water drag alone: dv/dt = -(v / CoastTimeConstant + MinDeceleration).</summary>
    public float DecelerationAt(float speed) => speed / CoastTimeConstant + MinDeceleration;

    /// <summary>
    /// How far the ship drifts before coming to rest from <paramref name="speed"/> under drag alone:
    /// the integral of v dt under <see cref="DecelerationAt"/>, i.e. tau * (v - k * ln(1 + v / k)) with k = c * tau.
    /// </summary>
    public float StoppingDistance(float speed)
    {
        var k = MinDeceleration * CoastTimeConstant;
        return CoastTimeConstant * (speed - k * MathF.Log(1f + speed / k));
    }

    /// <summary>
    /// Fastest speed from which the ship drifts to rest within <paramref name="distance"/>: the inverse of
    /// <see cref="StoppingDistance"/>, found by bisection (it has no closed form). Capped at <see cref="MaxSpeed"/>.
    /// </summary>
    public float SpeedForStoppingDistance(float distance)
    {
        if (distance <= 0f)
            return 0f;
        if (StoppingDistance(MaxSpeed) <= distance)
            return MaxSpeed;

        float lo = 0f, hi = MaxSpeed;
        for (var i = 0; i < 24; i++)
        {
            var mid = (lo + hi) / 2f;
            if (StoppingDistance(mid) <= distance) lo = mid;
            else hi = mid;
        }
        return lo;
    }

    public float TurnRadiusAt(float speed)
    {
        var fraction = MaxSpeed > 0f ? Math.Clamp(speed / MaxSpeed, 0f, 1f) : 0f;
        return MinTurnRadius + (TurnRadiusAtMaxSpeed - MinTurnRadius) * fraction;
    }
}
