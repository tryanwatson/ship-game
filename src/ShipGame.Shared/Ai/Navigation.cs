using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Ai;

/// <summary>
/// Look-ahead obstacle avoidance for NPC helms. Before committing to a heading, preview the arc the ship would
/// actually sail to get onto it (turning at its real turning radius) and, if that runs onto land, take the
/// nearest heading that doesn't.
/// </summary>
public static class Navigation
{
    /// <summary>How far ahead (seconds) to preview each candidate course.</summary>
    public const float LookaheadSeconds = 3f;

    /// <summary>Required distance from ship center to the shore along the previewed path: half a hull plus margin.</summary>
    public const float Clearance = 2f;

    private const float PreviewStepSeconds = 0.2f;
    private const float CandidateStepDegrees = 15f;

    // Preview at no less than this speed, so a ship gathering way still looks where it's about to go.
    private const float MinPreviewSpeed = 2f;

    /// <summary>
    /// The heading closest to <paramref name="desiredHeading"/> whose previewed path stays clear of land.
    /// <paramref name="preferredSide"/> (+1 starboard / -1 port / 0 none) carries between calls so that, with an
    /// obstacle dead ahead, the ship keeps going round the side it already chose instead of dithering.
    /// </summary>
    public static float ChooseHeading(World world, Ship ship, float desiredHeading, ref int preferredSide)
    {
        var previewSpeed = MathF.Max(ship.Speed, MinPreviewSpeed);
        var reach = previewSpeed * LookaheadSeconds + Clearance;
        if (world.DistanceToLand(ship.Position) > reach)
        {
            preferredSide = 0;
            return desiredHeading; // open water: nothing to avoid
        }

        var bestHeading = desiredHeading;
        var bestClearTime = -1f;
        var steps = (int)(180f / CandidateStepDegrees);
        for (var step = 0; step <= steps; step++)
        {
            // Try 0, then +/- each step, preferred side first.
            for (var sign = 0; sign < (step == 0 ? 1 : 2); sign++)
            {
                var side = step == 0 ? 0 : (sign == 0 ? (preferredSide == 0 ? 1 : preferredSide) : -(preferredSide == 0 ? 1 : preferredSide));
                var candidate = Angles.Wrap(desiredHeading + side * step * CandidateStepDegrees * MathF.PI / 180f);

                var clearTime = ClearTime(world, ship, candidate, previewSpeed);
                if (clearTime >= LookaheadSeconds)
                {
                    preferredSide = side == 0 ? preferredSide : side;
                    return candidate;
                }
                if (clearTime > bestClearTime)
                {
                    bestClearTime = clearTime;
                    bestHeading = candidate;
                }
            }
        }

        // Boxed in: take whichever course keeps us off the rocks longest.
        return bestHeading;
    }

    /// <summary>
    /// Whether a straight line between two points is blocked by land (for line-of-fire checks), other than
    /// <paramref name="ignoredIslandId"/>.
    /// </summary>
    public static bool LineBlockedByLand(World world, Vector2 from, Vector2 to, int? ignoredIslandId = null) =>
        world.LineHitsLand(from, to, Projectile.DefaultRadius, ignoredIslandId);

    /// <summary>
    /// Seconds of the preview before the ship would come within <see cref="Clearance"/> of land: turning onto
    /// <paramref name="heading"/> at full rudder, then holding it.
    /// </summary>
    private static float ClearTime(World world, Ship ship, float heading, float speed)
    {
        var position = ship.Position;
        var current = ship.Heading;
        var maxTurn = speed * PreviewStepSeconds / ship.Stats.TurnRadiusAt(speed);
        var stepLength = speed * PreviewStepSeconds;

        for (var t = PreviewStepSeconds; t <= LookaheadSeconds + 1e-4f; t += PreviewStepSeconds)
        {
            current = Angles.Wrap(current + Math.Clamp(Angles.Delta(current, heading), -maxTurn, maxTurn));
            position += new Vector2(MathF.Cos(current), MathF.Sin(current)) * stepLength;
            if (world.DistanceToLand(position) < Clearance)
                return t - PreviewStepSeconds;
        }
        return LookaheadSeconds;
    }
}
