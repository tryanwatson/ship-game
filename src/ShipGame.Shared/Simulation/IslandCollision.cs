using System.Numerics;

namespace ShipGame.Shared.Simulation;

/// <summary>Keeps hulls out of islands, and makes running aground hurt.</summary>
public static class IslandCollision
{
    /// <summary>Damage for running aground: 10% of a fresh Sloop's health, the player's starting ship.</summary>
    public static readonly float GroundingDamage = 0.1f * ShipStats.Sloop.MaxHealth;

    /// <summary>
    /// Only impacts faster than this (speed into the shore, tiles/s) do damage, so scraping along a coast,
    /// nudging into it from rest, or being blown onto it by the wind are harmless.
    /// </summary>
    public const float DamagingImpactSpeed = 1f;

    public static void Resolve(Ship ship, IReadOnlyList<Island> islands)
    {
        var wasAground = ship.IsAground;
        ship.IsAground = false;
        if (islands.Count == 0)
            return;

        Span<Vector2> hull = stackalloc Vector2[HullShape.PointCount];
        var hullReach = ship.Stats.Length / 2f;

        foreach (var island in islands)
        {
            if (Vector2.Distance(ship.Position, island.Center) > island.BoundingRadius + hullReach)
                continue;

            HullShape.GetWorldOutline(ship.Position, ship.Heading, ship.Stats, hull);
            if (!Geometry.TryGetPenetration(hull, island.Outline, out var normal, out var depth))
                continue;

            // Damage is judged on how hard we were going into the shore, and only on first contact: a ship
            // scraping along a coast with its bow still angled in would otherwise be hit again every tick.
            var impactSpeed = -Vector2.Dot(ship.Velocity, normal);
            if (impactSpeed > DamagingImpactSpeed && !wasAground && !ship.IsAground)
                ship.Health = MathF.Max(0f, ship.Health - GroundingDamage);
            ship.IsAground = true;

            // Back out of the land, then lose the part of our way that was driving into it. Ships only move
            // bow-first, so keep the along-shore fraction: head-on stops dead, a glancing blow scrapes along.
            ship.Position += normal * depth;
            var into = -Vector2.Dot(ship.Forward, normal);
            if (into > 0f)
                ship.Speed *= MathF.Sqrt(MathF.Max(0f, 1f - into * into));

            var driftInto = Vector2.Dot(ship.WindDrift, normal);
            if (driftInto < 0f)
                ship.WindDrift -= normal * driftInto;
        }
    }
}
