using ShipGame.Shared.Simulation;

namespace ShipGame.Net;

public static class Protocol
{
    /// <summary>Sent with every connection request; a mismatch is refused, so stale clients get a clear message.</summary>
    public const string Key = "ShipGame";

    /// <summary>Bump whenever the wire format changes.</summary>
    public const int Version = 1;

    public const int DefaultPort = 7777;

    public const int MaxPlayers = 12;

    /// <summary>Snapshots go out every this many simulation ticks (30 Hz / 2 = 15 Hz).</summary>
    public const int SnapshotEveryTicks = 2;

    /// <summary>Ships per snapshot packet, keeping each unreliable packet comfortably under one MTU.</summary>
    public const int ShipsPerSnapshotChunk = 12;

    public static readonly int TickRate = SimConstants.TickRate;
}

/// <summary>First byte of every packet.</summary>
public enum MessageType : byte
{
    // Server -> client
    Welcome = 1,
    Lobby = 2,
    RunStarted = 3,
    ShipInfo = 4,
    Events = 5,
    SnapshotChunk = 6,

    // Client -> server
    Command = 100,
    Ready = 101,
}
