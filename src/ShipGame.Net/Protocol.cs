using ShipGame.Shared.Simulation;

namespace ShipGame.Net;

public static class Protocol
{
    /// <summary>Sent with every connection request; a mismatch is refused, so stale clients get a clear message.</summary>
    public const string Key = "ShipGame";

    /// <summary>Bump whenever the wire format changes.</summary>
    public const int Version = 23;

    public const int DefaultPort = 7777;

    public const int MaxPlayers = 12;

    /// <summary>Snapshots go out every this many simulation ticks (30 Hz / 2 = 15 Hz).</summary>
    public const int SnapshotEveryTicks = 2;

    /// <summary>
    /// Largest snapshot packet. LiteNetLib (2.x) sends unreliable packets of at most 1023 bytes of payload and throws
    /// on anything bigger, so chunks are packed by size, leaving room to spare.
    /// </summary>
    public const int MaxSnapshotChunkBytes = 1000;

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
    SetName = 102,
    SetStartingGold = 103,
}
