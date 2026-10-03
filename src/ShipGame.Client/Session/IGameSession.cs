using System.Collections.Generic;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Simulation;

namespace ShipGame.Client.Session;

/// <summary>
/// The client's only route into the game. Today it's <see cref="LocalGameSession"/>; a networked
/// session will implement the same contract (send commands to the server, expose a predicted world).
/// </summary>
public interface IGameSession
{
    int LocalPlayerId { get; }

    /// <summary>The world state to render.</summary>
    World World { get; }

    /// <summary>How far (0..1) render time is between the previous tick and the current one.</summary>
    float InterpolationAlpha { get; }

    void Send(Command command);

    /// <summary>
    /// World events that arrived since the last call (locally: from the steps just run; online: from the server).
    /// For effects and sounds; game state itself is already reflected in <see cref="World"/>.
    /// </summary>
    IReadOnlyList<WorldEvent> TakeEvents();

    void Update(double elapsedSeconds);
}
