using System.Numerics;

namespace ShipGame.Shared.Simulation;

public sealed class Projectile
{
    public const float Radius = 0.15f;

    public Projectile(int id, int ownerShipId, Team team, float damage)
    {
        Id = id;
        OwnerShipId = ownerShipId;
        Team = team;
        Damage = damage;
    }

    public int Id { get; }

    /// <summary>The ship that fired it; projectiles never hit their own ship.</summary>
    public int OwnerShipId { get; }

    /// <summary>The firing ship's team; projectiles pass harmlessly through friendly ships.</summary>
    public Team Team { get; }

    public float Damage { get; }

    public Vector2 Position { get; set; }

    public Vector2 PreviousPosition { get; set; }

    public Vector2 Velocity { get; set; }

    public int RemainingTicks { get; set; }
}
