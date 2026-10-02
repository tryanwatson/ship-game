namespace ShipGame.Shared.Simulation;

public static class Angles
{
    /// <summary>Wraps an angle into the range (-PI, PI].</summary>
    public static float Wrap(float radians)
    {
        radians %= MathF.Tau;
        if (radians <= -MathF.PI) radians += MathF.Tau;
        else if (radians > MathF.PI) radians -= MathF.Tau;
        return radians;
    }

    /// <summary>Shortest signed rotation from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static float Delta(float from, float to) => Wrap(to - from);

    public static float Lerp(float from, float to, float t) => Wrap(from + Delta(from, to) * t);
}
