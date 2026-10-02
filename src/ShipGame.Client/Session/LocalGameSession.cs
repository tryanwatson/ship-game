using System;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Simulation;

namespace ShipGame.Client.Session;

/// <summary>Single-player: runs the authoritative world in-process on a fixed tick.</summary>
public sealed class LocalGameSession : IGameSession
{
    // Avoid a spiral of catch-up ticks after a long hitch (debugger pause, window drag).
    private const double MaxFrameSeconds = 0.25;

    private double _accumulator;

    public LocalGameSession(World world, int localPlayerId)
    {
        World = world;
        LocalPlayerId = localPlayerId;
    }

    public int LocalPlayerId { get; }

    public World World { get; }

    public float InterpolationAlpha => (float)(_accumulator / SimConstants.TickDelta);

    public void Send(Command command) => World.Enqueue(command);

    public void Update(double elapsedSeconds)
    {
        _accumulator += Math.Min(elapsedSeconds, MaxFrameSeconds);
        while (_accumulator >= SimConstants.TickDelta)
        {
            World.Step();
            _accumulator -= SimConstants.TickDelta;
        }
    }
}
