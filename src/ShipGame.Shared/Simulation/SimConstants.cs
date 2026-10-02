namespace ShipGame.Shared.Simulation;

public static class SimConstants
{
    // The simulation always advances in fixed ticks so the same code can later run on
    // a dedicated server and stay in step with client-side prediction.
    public const int TickRate = 30;
    public const float TickDelta = 1f / TickRate;
}
