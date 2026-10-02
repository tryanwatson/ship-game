using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Ai;

/// <summary>
/// Sails endless laps of a circle by chasing a point a fixed angle ahead on it. The circle should be wider
/// than the ship's turning radius at its sail setting, or it will sail a wider one.
/// </summary>
public sealed class CirclePatrol : INpcBehavior
{
    // How far around the circle the aim point leads the ship. Far enough that the autopilot never thinks it
    // is about to arrive and takes in sail; close enough to hug the circle.
    private const float LeadAngle = MathF.PI / 4f;

    public CirclePatrol(Vector2 center, float radius, bool clockwise = true)
    {
        Center = center;
        Radius = radius;
        Clockwise = clockwise;
    }

    public Vector2 Center { get; }

    public float Radius { get; }

    /// <summary>Clockwise on screen (Y-down world).</summary>
    public bool Clockwise { get; }

    public void Update(World world, Ship ship)
    {
        // Chasing a point on the circle itself settles on a smaller circle, radius R * cos(lead): the ship ends up
        // pointing straight at the aim point. Pushing the aim point out to R / cos(lead) makes that steady state
        // exactly R, and it self-corrects: inside the circle the aim point bears outward, outside it bears inward.
        var offset = ship.Position - Center;
        var angle = MathF.Atan2(offset.Y, offset.X) + (Clockwise ? LeadAngle : -LeadAngle);
        ship.MoveTarget = Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (Radius / MathF.Cos(LeadAngle));
    }

    /// <summary>A pose on the circle, facing along it, for spawning a patrolling ship already under way.</summary>
    public (Vector2 Position, float Heading) StartPose(float angle) =>
        (Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Radius,
         Angles.Wrap(angle + (Clockwise ? MathF.PI / 2f : -MathF.PI / 2f)));
}
