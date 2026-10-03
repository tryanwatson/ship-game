using LiteNetLib;
using LiteNetLib.Utils;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Simulation;

namespace ShipGame.Net;

public enum ConnectionStatus
{
    Connecting,

    /// <summary>Connected and waiting in the lobby (before a run, or after one ends).</summary>
    Lobby,

    InRun,

    Disconnected,
}

/// <summary>
/// The client end of a server connection: sends commands and readiness, receives lobby updates, ship info,
/// events, and snapshots, and keeps a <see cref="ClientReplica"/> of the world up to date. Call
/// <see cref="Update"/> every frame; everything happens on the calling thread.
/// </summary>
public sealed class ClientConnection : IDisposable
{
    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _net;
    private readonly NetDataWriter _writer = new();
    private readonly Dictionary<long, (Snapshot Assembled, int Received, int Expected)> _partialSnapshots = new();
    private NetPeer? _server;

    public ClientConnection(string host, int port)
    {
        _net = new NetManager(_listener) { DisconnectTimeout = 10_000 };
        _listener.PeerConnectedEvent += peer => _server = peer;
        _listener.PeerDisconnectedEvent += OnDisconnected;
        _listener.NetworkReceiveEvent += OnReceive;
        _net.Start();

        var hello = new NetDataWriter();
        hello.Put(Protocol.Key);
        hello.Put(Protocol.Version);
        _net.Connect(host, port, hello);
    }

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Connecting;

    /// <summary>Why the connection ended (refused, timed out, server shut down...).</summary>
    public string? DisconnectReason { get; private set; }

    /// <summary>Assigned by the server on arrival; 0 until then.</summary>
    public int LocalPlayerId { get; private set; }

    public LobbyState? Lobby { get; private set; }

    public ClientReplica Replica { get; } = new();

    /// <summary>Round-trip time to the server in milliseconds.</summary>
    public int RoundTripMs => _server?.RoundTripTime ?? 0;

    public void Update(double elapsedSeconds)
    {
        _net.PollEvents();
        Replica.Advance(elapsedSeconds);
    }

    public void Send(Command command)
    {
        if (_server is null || Status != ConnectionStatus.InRun)
            return;
        _writer.Reset();
        _writer.Put((byte)MessageType.Command);
        _writer.PutCommand(command);
        _server.Send(_writer, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>Ready up in the lobby; the run starts once everyone is ready.</summary>
    public void SetReady(bool ready = true)
    {
        if (_server is null)
            return;
        _writer.Reset();
        _writer.Put((byte)MessageType.Ready);
        _writer.Put(ready);
        _server.Send(_writer, DeliveryMethod.ReliableOrdered);
    }

    public IReadOnlyList<WorldEvent> TakeEvents() => Replica.TakeEvents();

    public void Dispose() => _net.Stop();

    private void OnDisconnected(NetPeer peer, DisconnectInfo info)
    {
        Status = ConnectionStatus.Disconnected;
        _server = null;
        DisconnectReason = info.AdditionalData is { AvailableBytes: > 0 } data
            ? data.GetString()
            : info.Reason switch
            {
                LiteNetLib.DisconnectReason.ConnectionFailed => "COULD NOT REACH SERVER",
                LiteNetLib.DisconnectReason.UnknownHost => "UNKNOWN SERVER ADDRESS",
                LiteNetLib.DisconnectReason.Timeout => "CONNECTION TIMED OUT",
                LiteNetLib.DisconnectReason.RemoteConnectionClose => "SERVER CLOSED THE CONNECTION",
                _ => info.Reason.ToString().ToUpperInvariant(),
            };
    }

    private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod delivery)
    {
        try
        {
            switch ((MessageType)reader.GetByte())
            {
                case MessageType.Welcome:
                    LocalPlayerId = reader.GetInt();
                    Status = ConnectionStatus.Lobby;
                    break;
                case MessageType.Lobby:
                    Lobby = reader.GetLobby();
                    if (!Lobby.RunInProgress && Status == ConnectionStatus.InRun)
                        Status = ConnectionStatus.Lobby; // run over: back to the lobby (the replica keeps showing the wreckage)
                    break;
                case MessageType.RunStarted:
                    Replica.Reset(reader.GetRunStart());
                    _partialSnapshots.Clear();
                    Status = ConnectionStatus.InRun;
                    break;
                case MessageType.ShipInfo:
                    Replica.EnqueueShipInfo(reader.GetShipInfo());
                    break;
                case MessageType.Events:
                {
                    var count = reader.GetUShort();
                    var events = new List<WorldEvent>(count);
                    for (var i = 0; i < count; i++)
                        events.Add(reader.GetEvent());
                    Replica.EnqueueEvents(events);
                    break;
                }
                case MessageType.SnapshotChunk:
                    OnSnapshotChunk(reader.GetSnapshotChunk());
                    break;
            }
        }
        finally
        {
            reader.Recycle();
        }
    }

    /// <summary>Collects a snapshot's chunks; hands it to the replica once all have arrived. Incomplete ones are dropped once a newer one completes.</summary>
    private void OnSnapshotChunk(Wire.SnapshotChunk chunk)
    {
        if (chunk.Tick <= Replica.LatestSnapshotTick)
            return;

        if (!_partialSnapshots.TryGetValue(chunk.Tick, out var entry))
            entry = (new Snapshot { Tick = chunk.Tick }, 0, chunk.Count);

        var assembled = entry.Assembled;
        if (chunk.Index == 0)
        {
            assembled.Wind = chunk.Partial.Wind;
            assembled.Wave = chunk.Partial.Wave;
            assembled.TicksUntilNextWave = chunk.Partial.TicksUntilNextWave;
            assembled.RunOver = chunk.Partial.RunOver;
            assembled.Players = chunk.Partial.Players;
            assembled.IslandCooldowns = chunk.Partial.IslandCooldowns;
        }
        assembled.Ships.AddRange(chunk.Partial.Ships);
        entry = (assembled, entry.Received + 1, entry.Expected);

        if (entry.Received < entry.Expected)
        {
            _partialSnapshots[chunk.Tick] = entry;
            return;
        }

        _partialSnapshots.Remove(chunk.Tick);
        foreach (var stale in _partialSnapshots.Keys.Where(t => t < chunk.Tick).ToList())
            _partialSnapshots.Remove(stale);
        Replica.AddSnapshot(assembled);
    }
}
