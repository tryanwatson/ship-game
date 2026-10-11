using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using LiteNetLib;
using LiteNetLib.Utils;
using ShipGame.Net;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Server;

/// <summary>
/// The authoritative game server. Players gather in a lobby; once everyone is ready a run starts and the server
/// steps the world at the simulation rate, applying commands from each connection (always on behalf of that
/// connection's own player), and sends out ship info, events, and snapshots. When the run ends (a wipe, or
/// everyone leaves) it's back to the lobby. Single-threaded: call <see cref="Tick"/> at the tick rate.
/// </summary>
public sealed class GameServer : IDisposable
{
    private sealed class RemotePlayer
    {
        public required NetPeer Peer { get; init; }
        public required int PlayerId { get; init; }
        public bool Ready { get; set; }

        /// <summary>What they're called; it has to be given before readying up, and carries over from run to run.</summary>
        public string Name { get; set; } = "";

        /// <summary>Messages this player may still send right now; refills every tick (a token bucket).</summary>
        public float MessageBudget { get; set; } = MessageBurst;

        public int DroppedMessages { get; set; }

        /// <summary>Sequence number of the newest command received (queued for the next step).</summary>
        public uint LastCommandReceived { get; set; }

        /// <summary>Sequence number of the newest command the world has applied; sent back in snapshots.</summary>
        public uint LastCommandApplied { get; set; }
    }

    // Rate limit on commands and lobby messages per player: a sustained MessagesPerSecond, bursts up to
    // MessageBurst. The client sends at most one move order per tick plus key presses, far below this; anything
    // over it is dropped.
    public const float MessagesPerSecond = 60f;
    public const float MessageBurst = 120f;

    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _net;
    private readonly Dictionary<int, RemotePlayer> _byPeerId = new();
    private readonly Dictionary<int, int> _sentStatsVersions = new();
    private readonly Relevance _relevance = new();
    private readonly List<Ship> _enteredRange = new();
    private readonly List<int> _leftRange = new();
    private readonly NetDataWriter _writer = new();
    private readonly Action<string> _log;
    private readonly byte[]? _password;
    private int _nextPlayerId = 1;
    private uint _snapshotSequence;
    private long _stepsThisRun;
    private int _runSeed = Environment.TickCount;

    /// <param name="password">Required to join; null or empty for an open server.</param>
    /// <param name="testing">Runs open with a late game's worth of cards to choose (see <see cref="Testing"/>).</param>
    public GameServer(int port = Protocol.DefaultPort, Action<string>? log = null, bool friendlyFire = true, string? password = null,
        bool testing = false)
    {
        FriendlyFire = friendlyFire;
        Testing = testing;
        _password = string.IsNullOrEmpty(password) ? null : Encoding.UTF8.GetBytes(password);
        _log = log ?? (_ => { });
        _net = new NetManager(_listener) { DisconnectTimeout = 10_000 };
        _listener.ConnectionRequestEvent += OnConnectionRequest;
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnReceive;
        if (!_net.Start(port))
            throw new InvalidOperationException($"Could not listen on port {port}.");
        Port = _net.LocalPort;
    }

    public int Port { get; }

    /// <summary>PvP: players' shots hurt other players. Applies from the next run.</summary>
    public bool FriendlyFire { get; set; }

    /// <summary>Gold every player starts a run with. Set from the lobby by any player, for playtesting.</summary>
    public int StartingGold { get; private set; }

    /// <summary>
    /// Playtesting: each run opens with a late game's worth of hands to choose from, rerolled freely (see
    /// <see cref="CardRewards.OfferTesting"/>). Set by whoever runs the server (the host), not from the lobby.
    /// </summary>
    public bool Testing { get; }

    /// <summary>The run in progress, or null in the lobby.</summary>
    public World? World { get; private set; }

    public int PlayerCount => _byPeerId.Count;

    /// <summary>Seed for the next run; tests fix it for reproducibility.</summary>
    public int NextRunSeed { set => _runSeed = value; }

    /// <summary>Handles network traffic without advancing the game. Cheap; call as often as you like.</summary>
    public void Poll() => _net.PollEvents();

    /// <summary>One simulation tick: handle traffic, start a run if everyone's ready, step, and send.</summary>
    public void Tick()
    {
        _net.PollEvents();
        foreach (var player in _byPeerId.Values)
            player.MessageBudget = MathF.Min(MessageBurst, player.MessageBudget + MessagesPerSecond / SimConstants.TickRate);

        if (World is null)
        {
            if (_byPeerId.Count > 0 && _byPeerId.Values.All(p => p.Ready))
                StartRun();
            return;
        }

        World.Step();
        _stepsThisRun++;
        foreach (var player in _byPeerId.Values)
            player.LastCommandApplied = player.LastCommandReceived; // every queued command was applied in that step
        SendWorldOutput(World);

        if (World.IsRunOver)
            EndRun(World.IsVictory ? "the last boss was sunk" : "the crew was sunk");
    }

    public void Dispose() => _net.Stop();

    // ---- Lobby ------------------------------------------------------------------------------------------

    private void OnConnectionRequest(ConnectionRequest request)
    {
        string? refusal = null;
        try
        {
            if (request.Data.GetString() != Protocol.Key || request.Data.GetInt() != Protocol.Version)
                refusal = "VERSION MISMATCH - UPDATE YOUR GAME";
            else if (_password is not null && !PasswordMatches(request.Data.GetString()))
                refusal = "WRONG PASSWORD";
        }
        catch
        {
            refusal = "VERSION MISMATCH - UPDATE YOUR GAME";
        }

        refusal ??= World is not null ? "RUN IN PROGRESS - TRY AGAIN SOON"
            : _byPeerId.Count >= Protocol.MaxPlayers ? "SERVER FULL"
            : null;

        if (refusal is null)
        {
            request.Accept();
            return;
        }

        var reason = new NetDataWriter();
        reason.Put(refusal);
        request.Reject(reason);
        _log($"Refused {request.RemoteEndPoint}: {refusal}");
    }

    private bool PasswordMatches(string given) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), _password);

    private void OnPeerConnected(NetPeer peer)
    {
        var player = new RemotePlayer { Peer = peer, PlayerId = _nextPlayerId++ };
        _byPeerId[peer.Id] = player;
        _log($"Player {player.PlayerId} joined from {peer}");

        _writer.Reset();
        _writer.Put((byte)MessageType.Welcome);
        _writer.Put(player.PlayerId);
        peer.Send(_writer, DeliveryMethod.ReliableOrdered);
        BroadcastLobby();
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        if (!_byPeerId.Remove(peer.Id, out var player))
            return;
        _log($"Player {player.PlayerId} left ({info.Reason})");

        if (World is not null)
        {
            World.RemovePlayer(player.PlayerId);
            if (_byPeerId.Count == 0)
            {
                EndRun("everyone left");
                return;
            }
            SendWorldOutput(World); // tell the others the ship is gone
        }
        BroadcastLobby();
    }

    private void BroadcastLobby()
    {
        var lobby = new LobbyState(World is not null,
            _byPeerId.Values.OrderBy(p => p.PlayerId).Select(p => new LobbyPlayer(p.PlayerId, p.Ready, p.Name)).ToList(),
            FriendlyFire, StartingGold, Testing);
        _writer.Reset();
        _writer.Put((byte)MessageType.Lobby);
        _writer.PutLobby(lobby);
        SendToAll(_writer, DeliveryMethod.ReliableOrdered);
    }

    // ---- Runs -------------------------------------------------------------------------------------------

    private void StartRun()
    {
        var seed = _runSeed++;
        var players = _byPeerId.Values.OrderBy(p => p.PlayerId).ToList();
        var crew = players
            .Select(p => (p.PlayerId, p.Name)) // everyone ready means everyone's named
            .ToList();
        var world = Runs.Create(seed, crew, FriendlyFire, StartingGold, Testing);
        foreach (var player in players)
        {
            player.Ready = false;
        }

        World = world;
        _sentStatsVersions.Clear();
        _relevance.Clear();
        _log($"Run started with {players.Count} player(s), friendly fire {(FriendlyFire ? "on" : "off")}, starting gold {StartingGold}{(Testing ? ", testing (late-game cards)" : "")}");

        _writer.Reset();
        _writer.Put((byte)MessageType.RunStarted);
        _writer.PutRunStart(new RunStart(world.Tick, world.WorldSize, world.Wind, world.FriendlyFire, crew));
        SendToAll(_writer, DeliveryMethod.ReliableOrdered);
        BroadcastLobby();
        SendWorldOutput(world); // the starting ships, before the first snapshot
    }

    private void EndRun(string why)
    {
        _log($"Run ended: {why}");
        World = null;
        foreach (var player in _byPeerId.Values)
            player.Ready = false;
        BroadcastLobby();
    }

    // ---- Sending ------------------------------------------------------------------------------------------

    /// <summary>
    /// Ship info first (so every event and snapshot refers to ships the client knows), then this tick's
    /// events on the same reliable channel, then the snapshot (unreliable: a lost one is replaced by the next).
    /// Only ships near some player are sent (see <see cref="Relevance"/>): one coming into range is described
    /// afresh, and one going out of it is hidden.
    /// </summary>
    private void SendWorldOutput(World world)
    {
        _enteredRange.Clear();
        _leftRange.Clear();
        _relevance.Update(world, _enteredRange, _leftRange);
        foreach (var ship in _enteredRange)
            _sentStatsVersions.Remove(ship.Id);
        foreach (var id in _leftRange)
            _sentStatsVersions.Remove(id);

        foreach (var ship in world.Ships)
        {
            if (!_relevance.IsShown(ship.Id))
                continue;
            if (_sentStatsVersions.TryGetValue(ship.Id, out var version) && version == ship.StatsVersion)
                continue;
            _sentStatsVersions[ship.Id] = ship.StatsVersion;
            _writer.Reset();
            _writer.Put((byte)MessageType.ShipInfo);
            _writer.PutShipInfo(Snapshot.DescribeShip(ship, world.Tick));
            SendToAll(_writer, DeliveryMethod.ReliableOrdered);
        }

        var events = world.DrainEvents();
        foreach (var sunk in events.OfType<ShipSunk>())
            _sentStatsVersions.Remove(sunk.ShipId);

        // Rejections are private; everything else goes to everyone.
        var shared = events.Where(e => e is not CommandRejected).ToList();
        shared.AddRange(_leftRange.Select(id => new ShipHidden(world.Tick, id)));
        if (shared.Count > 0)
            SendEvents(shared, null);
        foreach (var rejection in events.OfType<CommandRejected>())
        {
            var recipient = _byPeerId.Values.FirstOrDefault(p => p.PlayerId == rejection.PlayerId);
            if (recipient is not null)
                SendEvents(new[] { rejection }, recipient.Peer);
        }

        // Counted in steps rather than world ticks, which stand still while the game is paused for cards.
        if (_stepsThisRun % Protocol.SnapshotEveryTicks == 0 || world.IsRunOver)
        {
            var snapshot = Snapshot.Capture(world, ship => _relevance.IsShown(ship.Id));
            snapshot.Sequence = ++_snapshotSequence;
            foreach (var player in _byPeerId.Values)
                snapshot.CommandAcks.Add((player.PlayerId, player.LastCommandApplied));
            foreach (var chunk in Wire.WriteSnapshotChunks(snapshot))
                SendToAll(chunk, DeliveryMethod.Unreliable);
        }
    }

    private void SendEvents(IReadOnlyList<WorldEvent> events, NetPeer? onlyTo)
    {
        // Batches stay small; reliable delivery fragments any that don't fit one packet.
        _writer.Reset();
        _writer.Put((byte)MessageType.Events);
        _writer.PutEvents(events);
        if (onlyTo is null)
            SendToAll(_writer, DeliveryMethod.ReliableOrdered);
        else
            onlyTo.Send(_writer, DeliveryMethod.ReliableOrdered);
    }

    private void SendToAll(NetDataWriter writer, DeliveryMethod delivery)
    {
        foreach (var player in _byPeerId.Values)
            player.Peer.Send(writer, delivery);
    }

    private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod delivery)
    {
        try
        {
            if (!_byPeerId.TryGetValue(peer.Id, out var player))
                return;

            if (player.MessageBudget < 1f)
            {
                // Logged on the first and then every 100th, so a flood can't flood the log too.
                if (player.DroppedMessages++ % 100 == 0)
                    _log($"Player {player.PlayerId} is sending too fast; dropped {player.DroppedMessages} message(s) so far");
                return;
            }
            player.MessageBudget -= 1f;

            switch ((MessageType)reader.GetByte())
            {
                case MessageType.Command when World is not null:
                {
                    var sequence = reader.GetUInt();
                    // The command is read on behalf of this connection's player, whatever it claims.
                    World.Enqueue(reader.GetCommand(player.PlayerId));
                    player.LastCommandReceived = sequence;
                    break;
                }
                case MessageType.Ready when World is null:
                    // No readying up without a name.
                    player.Ready = reader.GetBool() && player.Name.Length > 0;
                    BroadcastLobby();
                    break;
                case MessageType.SetStartingGold when World is null:
                {
                    var gold = Math.Clamp(reader.GetInt(), 0, Runs.MaxStartingGold);
                    if (gold != StartingGold)
                    {
                        StartingGold = gold;
                        _log($"Player {player.PlayerId} set starting gold to {gold}");
                        BroadcastLobby();
                    }
                    break;
                }
                case MessageType.SetName when World is null:
                {
                    var name = PlayerNames.Clean(reader.GetString(64));
                    if (name != player.Name)
                    {
                        player.Name = name;
                        if (name.Length == 0)
                            player.Ready = false;
                        BroadcastLobby();
                    }
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            _log($"Bad packet from player {(_byPeerId.TryGetValue(peer.Id, out var p) ? p.PlayerId : -1)}: {ex.Message}");
        }
        finally
        {
            reader.Recycle();
        }
    }
}
