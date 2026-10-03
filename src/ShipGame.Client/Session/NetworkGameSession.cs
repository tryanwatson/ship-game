using System;
using System.Collections.Generic;
using ShipGame.Net;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Simulation;

namespace ShipGame.Client.Session;

/// <summary>Online play: the world is a mirror of the server's, and commands go over the wire.</summary>
public sealed class NetworkGameSession : IGameSession, IDisposable
{
    public NetworkGameSession(string host, int port, NetworkConditions? conditions = null)
    {
        Host = host;
        Port = port;
        Connection = new ClientConnection(host, port, conditions);
    }

    public string Host { get; }

    public int Port { get; }

    public ClientConnection Connection { get; }

    public int LocalPlayerId => Connection.LocalPlayerId;

    public World World => Connection.Replica.World;

    public float InterpolationAlpha => Connection.Replica.Alpha;

    public void Send(Command command) => Connection.Send(command);

    public IReadOnlyList<WorldEvent> TakeEvents() => Connection.TakeEvents();

    public void Update(double elapsedSeconds) => Connection.Update(elapsedSeconds);

    public void Dispose() => Connection.Dispose();
}
