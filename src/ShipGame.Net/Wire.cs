using System.Numerics;
using LiteNetLib.Utils;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;

namespace ShipGame.Net;

/// <summary>
/// Hand-written binary encoding for everything on the wire. Plain floats for now; quantization is a phase 3
/// bandwidth job. Every Write has a matching Read directly below it.
/// </summary>
public static class Wire
{
    // ---- Primitives -------------------------------------------------------------------------------------

    public static void Put(this NetDataWriter w, Vector2 v)
    {
        w.Put(v.X);
        w.Put(v.Y);
    }

    public static Vector2 GetVector2(this NetDataReader r) => new(r.GetFloat(), r.GetFloat());

    /// <summary>Ids are positive; -1 stands for "none".</summary>
    public static void PutOptional(this NetDataWriter w, int? value) => w.Put(value ?? -1);

    public static int? GetOptionalInt(this NetDataReader r)
    {
        var value = r.GetInt();
        return value < 0 ? null : value;
    }

    // ---- Commands (client -> server; PlayerId is never sent, the server knows who's talking) ----------

    private enum CommandTag : byte
    {
        Move = 1,
        Stop = 2,
        AdjustThrottle = 3,
        SetRudder = 4,
        CastAbility = 5,
        AnchorKey = 6,
        ChoosePlunder = 7,
        PurchaseUpgrade = 8,
    }

    public static void PutCommand(this NetDataWriter w, Command command)
    {
        switch (command)
        {
            case MoveCommand move:
                w.Put((byte)CommandTag.Move);
                w.Put(move.Target);
                break;
            case StopCommand:
                w.Put((byte)CommandTag.Stop);
                break;
            case AdjustThrottleCommand adjust:
                w.Put((byte)CommandTag.AdjustThrottle);
                w.Put((sbyte)Math.Clamp(adjust.Delta, sbyte.MinValue, sbyte.MaxValue));
                break;
            case SetRudderCommand rudder:
                w.Put((byte)CommandTag.SetRudder);
                w.Put((sbyte)Math.Clamp(rudder.Rudder, -1, 1));
                break;
            case CastAbilityCommand cast:
                w.Put((byte)CommandTag.CastAbility);
                w.Put((byte)cast.Slot);
                w.Put(cast.Target);
                break;
            case AnchorKeyCommand anchor:
                w.Put((byte)CommandTag.AnchorKey);
                w.Put(anchor.Pressed);
                break;
            case ChoosePlunderCommand:
                w.Put((byte)CommandTag.ChoosePlunder);
                break;
            case PurchaseUpgradeCommand purchase:
                w.Put((byte)CommandTag.PurchaseUpgrade);
                w.Put(purchase.UpgradeId);
                break;
            default:
                throw new ArgumentException($"No wire format for {command.GetType().Name}.");
        }
    }

    /// <summary>Reads a command on behalf of <paramref name="playerId"/>: whatever the client claims, it only commands itself.</summary>
    public static Command GetCommand(this NetDataReader r, int playerId) => (CommandTag)r.GetByte() switch
    {
        CommandTag.Move => new MoveCommand(playerId, r.GetVector2()),
        CommandTag.Stop => new StopCommand(playerId),
        CommandTag.AdjustThrottle => new AdjustThrottleCommand(playerId, r.GetSByte()),
        CommandTag.SetRudder => new SetRudderCommand(playerId, r.GetSByte()),
        CommandTag.CastAbility => new CastAbilityCommand(playerId, (AbilitySlot)r.GetByte(), r.GetVector2()),
        CommandTag.AnchorKey => new AnchorKeyCommand(playerId, r.GetBool()),
        CommandTag.ChoosePlunder => new ChoosePlunderCommand(playerId),
        CommandTag.PurchaseUpgrade => new PurchaseUpgradeCommand(playerId, r.GetString(64)),
        var tag => throw new InvalidDataException($"Unknown command tag {tag}."),
    };

    // ---- World events (server -> client) ----------------------------------------------------------------

    private enum EventTag : byte
    {
        ShipSpawned = 1,
        ShipSunk = 2,
        ProjectileSpawned = 3,
        ProjectileImpact = 4,
        AbilityCast = 5,
        ShipGrounded = 6,
        GoldChanged = 7,
        IslandPlundered = 8,
        UpgradePurchased = 9,
        WaveStarted = 10,
        CommandRejected = 11,
        PlayerSunk = 12,
        PlayerRespawned = 13,
        RunEnded = 14,
        AreaStrikeLaunched = 15,
        AreaStrikeImpact = 16,
        AreaDiscovered = 17,
    }

    public static void PutEvent(this NetDataWriter w, WorldEvent e)
    {
        switch (e)
        {
            case ShipSpawned x:
                Begin(w, EventTag.ShipSpawned, x); w.Put(x.ShipId);
                break;
            case ShipSunk x:
                Begin(w, EventTag.ShipSunk, x); w.Put(x.ShipId); w.PutOptional(x.KillerShipId);
                break;
            case ProjectileSpawned x:
                Begin(w, EventTag.ProjectileSpawned, x);
                w.Put(x.ProjectileId); w.Put(x.OwnerShipId); w.Put((byte)x.Team);
                w.Put(x.Position); w.Put(x.Velocity); w.Put(x.Damage); w.Put(x.LifetimeTicks); w.Put(x.Radius);
                break;
            case AreaStrikeLaunched x:
                Begin(w, EventTag.AreaStrikeLaunched, x);
                w.Put(x.StrikeId); w.Put(x.OwnerShipId); w.Put((byte)x.Team);
                w.Put(x.Origin); w.Put(x.Target); w.Put(x.Radius); w.Put(x.Damage); w.Put(x.ImpactTick);
                break;
            case AreaStrikeImpact x:
                Begin(w, EventTag.AreaStrikeImpact, x); w.Put(x.StrikeId); w.Put(x.Target); w.Put(x.Radius);
                break;
            case AreaDiscovered x:
                Begin(w, EventTag.AreaDiscovered, x);
                w.Put((byte)x.Team);
                w.Put((ushort)x.Cells.Count);
                foreach (var cell in x.Cells)
                    w.Put((ushort)cell);
                break;
            case ProjectileImpact x:
                Begin(w, EventTag.ProjectileImpact, x); w.Put(x.ProjectileId); w.PutOptional(x.ShipId);
                break;
            case AbilityCast x:
                Begin(w, EventTag.AbilityCast, x); w.Put(x.ShipId); w.Put((byte)x.Slot); w.Put(x.CooldownTicks); w.Put((byte)x.Channel);
                break;
            case ShipGrounded x:
                Begin(w, EventTag.ShipGrounded, x); w.Put(x.ShipId);
                break;
            case GoldChanged x:
                Begin(w, EventTag.GoldChanged, x); w.Put(x.PlayerId); w.Put(x.Gold); w.Put(x.Delta);
                break;
            case IslandPlundered x:
                Begin(w, EventTag.IslandPlundered, x); w.Put(x.IslandId); w.Put(x.PlayerId); w.Put(x.Gold); w.Put(x.CooldownTicks);
                break;
            case UpgradePurchased x:
                Begin(w, EventTag.UpgradePurchased, x); w.Put(x.ShipId); w.Put(x.UpgradeId); w.Put(x.Level);
                break;
            case WaveStarted x:
                Begin(w, EventTag.WaveStarted, x); w.Put(x.Wave); w.Put(x.Pirates);
                break;
            case CommandRejected x:
                Begin(w, EventTag.CommandRejected, x); w.Put(x.PlayerId); w.PutCommand(x.Command); w.Put((byte)x.Reason);
                break;
            case PlayerSunk x:
                Begin(w, EventTag.PlayerSunk, x); w.Put(x.PlayerId); w.Put(x.RespawnTicks);
                break;
            case PlayerRespawned x:
                Begin(w, EventTag.PlayerRespawned, x); w.Put(x.PlayerId); w.Put(x.ShipId);
                break;
            case RunEnded x:
                Begin(w, EventTag.RunEnded, x);
                break;
            default:
                throw new ArgumentException($"No wire format for {e.GetType().Name}.");
        }
    }

    private static void Begin(NetDataWriter w, EventTag tag, WorldEvent e)
    {
        w.Put((byte)tag);
        w.Put(e.Tick);
    }

    public static WorldEvent GetEvent(this NetDataReader r)
    {
        var tag = (EventTag)r.GetByte();
        var tick = r.GetLong();
        switch (tag)
        {
            case EventTag.ShipSpawned: return new ShipSpawned(tick, r.GetInt());
            case EventTag.ShipSunk: return new ShipSunk(tick, r.GetInt(), r.GetOptionalInt());
            case EventTag.ProjectileSpawned:
                return new ProjectileSpawned(tick, r.GetInt(), r.GetInt(), (Team)r.GetByte(), r.GetVector2(), r.GetVector2(), r.GetFloat(), r.GetInt(), r.GetFloat());
            case EventTag.AreaStrikeLaunched:
                return new AreaStrikeLaunched(tick, r.GetInt(), r.GetInt(), (Team)r.GetByte(), r.GetVector2(), r.GetVector2(), r.GetFloat(), r.GetFloat(), r.GetLong());
            case EventTag.AreaStrikeImpact:
                return new AreaStrikeImpact(tick, r.GetInt(), r.GetVector2(), r.GetFloat());
            case EventTag.AreaDiscovered:
            {
                var team = (Team)r.GetByte();
                var count = r.GetUShort();
                var cells = new int[count];
                for (var i = 0; i < count; i++)
                    cells[i] = r.GetUShort();
                return new AreaDiscovered(tick, team, cells);
            }
            case EventTag.ProjectileImpact: return new ProjectileImpact(tick, r.GetInt(), r.GetOptionalInt());
            case EventTag.AbilityCast: return new AbilityCast(tick, r.GetInt(), (AbilitySlot)r.GetByte(), r.GetInt(), r.GetByte());
            case EventTag.ShipGrounded: return new ShipGrounded(tick, r.GetInt());
            case EventTag.GoldChanged: return new GoldChanged(tick, r.GetInt(), r.GetInt(), r.GetInt());
            case EventTag.IslandPlundered: return new IslandPlundered(tick, r.GetInt(), r.GetInt(), r.GetInt(), r.GetInt());
            case EventTag.UpgradePurchased: return new UpgradePurchased(tick, r.GetInt(), r.GetString(64), r.GetInt());
            case EventTag.WaveStarted: return new WaveStarted(tick, r.GetInt(), r.GetInt());
            case EventTag.CommandRejected:
            {
                var playerId = r.GetInt();
                var command = r.GetCommand(playerId);
                return new CommandRejected(tick, playerId, command, (RejectionReason)r.GetByte());
            }
            case EventTag.PlayerSunk: return new PlayerSunk(tick, r.GetInt(), r.GetInt());
            case EventTag.PlayerRespawned: return new PlayerRespawned(tick, r.GetInt(), r.GetInt());
            case EventTag.RunEnded: return new RunEnded(tick);
            default: throw new InvalidDataException($"Unknown event tag {tag}.");
        }
    }

    // ---- Lobby / run start ------------------------------------------------------------------------------

    public static void PutLobby(this NetDataWriter w, LobbyState lobby)
    {
        w.Put(lobby.RunInProgress);
        w.Put(lobby.FriendlyFire);
        w.Put((byte)lobby.Players.Count);
        foreach (var player in lobby.Players)
        {
            w.Put(player.PlayerId);
            w.Put(player.Ready);
        }
    }

    public static LobbyState GetLobby(this NetDataReader r)
    {
        var running = r.GetBool();
        var friendlyFire = r.GetBool();
        var count = r.GetByte();
        var players = new List<LobbyPlayer>(count);
        for (var i = 0; i < count; i++)
            players.Add(new LobbyPlayer(r.GetInt(), r.GetBool()));
        return new LobbyState(running, players, friendlyFire);
    }

    public static void PutRunStart(this NetDataWriter w, RunStart start)
    {
        w.Put(start.Tick);
        w.Put(start.WorldSize);
        w.Put(start.Wind);
        w.Put(start.FriendlyFire);
    }

    public static RunStart GetRunStart(this NetDataReader r) => new(r.GetLong(), r.GetVector2(), r.GetVector2(), r.GetBool());

    // ---- Ship info --------------------------------------------------------------------------------------

    public static void PutShipInfo(this NetDataWriter w, ShipInfo info)
    {
        w.Put(info.Tick);
        w.Put(info.ShipId);
        w.PutOptional(info.OwnerPlayerId);
        w.Put((byte)info.Team);
        PutStats(w, info.BaseStats);
        w.Put((byte)info.AbilityIds.Count);
        foreach (var id in info.AbilityIds)
            w.Put(id ?? "");
        w.Put((ushort)info.Modifiers.Count);
        foreach (var m in info.Modifiers)
        {
            w.Put((byte)m.Stat);
            w.Put((byte)m.Kind);
            w.Put(m.Value);
            w.Put(m.Source);
        }
        w.Put(info.Position);
        w.Put(info.Heading);
    }

    public static ShipInfo GetShipInfo(this NetDataReader r)
    {
        var tick = r.GetLong();
        var shipId = r.GetInt();
        var owner = r.GetOptionalInt();
        var team = (Team)r.GetByte();
        var stats = GetStats(r);
        var abilityCount = r.GetByte();
        var abilities = new List<string?>(abilityCount);
        for (var i = 0; i < abilityCount; i++)
        {
            var id = r.GetString(64);
            abilities.Add(id.Length == 0 ? null : id);
        }
        var modifierCount = r.GetUShort();
        var modifiers = new List<StatModifier>(modifierCount);
        for (var i = 0; i < modifierCount; i++)
            modifiers.Add(new StatModifier((StatId)r.GetByte(), (ModifierKind)r.GetByte(), r.GetFloat(), r.GetString(64)));
        return new ShipInfo(tick, shipId, owner, team, stats, abilities, modifiers, r.GetVector2(), r.GetFloat());
    }

    private static void PutStats(NetDataWriter w, ShipStats s)
    {
        foreach (var value in new[]
                 {
                     s.MaxSpeed, s.Acceleration, s.CoastTimeConstant, s.MinDeceleration, s.MinTurnRadius, s.TurnRadiusAtMaxSpeed,
                     s.Radius, s.Length, s.Beam, s.MaxHealth, s.CooldownSpeed, s.WeaponDamage, s.ProjectileSpeed, s.WeaponRange,
                 })
            w.Put(value);
    }

    private static ShipStats GetStats(NetDataReader r) => new(
        MaxSpeed: r.GetFloat(), Acceleration: r.GetFloat(), CoastTimeConstant: r.GetFloat(), MinDeceleration: r.GetFloat(),
        MinTurnRadius: r.GetFloat(), TurnRadiusAtMaxSpeed: r.GetFloat(), Radius: r.GetFloat(), Length: r.GetFloat(),
        Beam: r.GetFloat(), MaxHealth: r.GetFloat(), CooldownSpeed: r.GetFloat(), WeaponDamage: r.GetFloat(),
        ProjectileSpeed: r.GetFloat(), WeaponRange: r.GetFloat());

    // ---- Snapshots (chunked: each chunk is one unreliable packet) ---------------------------------------

    /// <summary>
    /// Splits a snapshot into packets of at most <see cref="Protocol.ShipsPerSnapshotChunk"/> ships. The first
    /// chunk also carries the world-level header (players, waves, island cooldowns).
    /// </summary>
    public static List<NetDataWriter> WriteSnapshotChunks(Snapshot snapshot)
    {
        var chunkCount = Math.Max(1, (snapshot.Ships.Count + Protocol.ShipsPerSnapshotChunk - 1) / Protocol.ShipsPerSnapshotChunk);
        var chunks = new List<NetDataWriter>(chunkCount);
        for (var chunk = 0; chunk < chunkCount; chunk++)
        {
            var w = new NetDataWriter();
            w.Put((byte)MessageType.SnapshotChunk);
            w.Put(snapshot.Tick);
            w.Put((byte)chunk);
            w.Put((byte)chunkCount);

            if (chunk == 0)
            {
                w.Put(snapshot.Wind);
                w.Put(snapshot.Wave);
                w.Put(snapshot.TicksUntilNextWave);
                w.Put(snapshot.RunOver);
                w.Put((byte)snapshot.Players.Count);
                foreach (var p in snapshot.Players)
                {
                    w.Put(p.PlayerId); w.Put(p.Gold); w.Put(p.Kills); w.Put(p.RespawnTicks);
                }
                w.Put((byte)snapshot.IslandCooldowns.Count);
                foreach (var (islandId, ticks) in snapshot.IslandCooldowns)
                {
                    w.Put(islandId); w.Put(ticks);
                }
            }

            var ships = snapshot.Ships.Skip(chunk * Protocol.ShipsPerSnapshotChunk).Take(Protocol.ShipsPerSnapshotChunk).ToList();
            w.Put((byte)ships.Count);
            foreach (var ship in ships)
                PutShipState(w, ship);
            chunks.Add(w);
        }
        return chunks;
    }

    /// <summary>One chunk as read off the wire (after its <see cref="MessageType"/> byte).</summary>
    public sealed record SnapshotChunk(long Tick, int Index, int Count, Snapshot Partial);

    public static SnapshotChunk GetSnapshotChunk(this NetDataReader r)
    {
        var tick = r.GetLong();
        var index = r.GetByte();
        var count = r.GetByte();
        var snapshot = new Snapshot { Tick = tick };
        if (index == 0)
        {
            snapshot.Wind = r.GetVector2();
            snapshot.Wave = r.GetInt();
            snapshot.TicksUntilNextWave = r.GetInt();
            snapshot.RunOver = r.GetBool();
            var players = r.GetByte();
            for (var i = 0; i < players; i++)
                snapshot.Players.Add(new PlayerSnapshot(r.GetInt(), r.GetInt(), r.GetInt(), r.GetInt()));
            var islands = r.GetByte();
            for (var i = 0; i < islands; i++)
                snapshot.IslandCooldowns.Add((r.GetInt(), r.GetInt()));
        }

        var ships = r.GetByte();
        for (var i = 0; i < ships; i++)
            snapshot.Ships.Add(GetShipState(r));
        return new SnapshotChunk(tick, index, count, snapshot);
    }

    private static void PutShipState(NetDataWriter w, ShipState s)
    {
        w.Put(s.ShipId);
        w.Put(s.Position);
        w.Put(s.Heading);
        w.Put(s.Speed);
        w.Put(s.Health);
        w.Put(s.Throttle);
        w.Put(s.Rudder);
        w.Put((byte)s.Anchor);
        w.Put((ushort)Math.Clamp(s.AnchorRaiseTicks, 0, ushort.MaxValue));
        w.PutOptional(s.PlunderIslandId);
        w.Put((ushort)Math.Clamp(s.PlunderTicks, 0, ushort.MaxValue));
        w.Put((byte)s.Stance);
        w.Put(s.MoveTarget.HasValue);
        if (s.MoveTarget is { } target)
            w.Put(target);
        foreach (var channels in s.Cooldowns)
        {
            var count = channels?.Length ?? 0;
            w.Put((byte)count);
            for (var c = 0; c < count; c++)
            {
                w.Put((ushort)Math.Clamp(channels![c].Remaining, 0, ushort.MaxValue));
                w.Put((ushort)Math.Clamp(channels[c].Duration, 0, ushort.MaxValue));
            }
        }
    }

    private static ShipState GetShipState(NetDataReader r)
    {
        var s = new ShipState
        {
            ShipId = r.GetInt(),
            Position = r.GetVector2(),
            Heading = r.GetFloat(),
            Speed = r.GetFloat(),
            Health = r.GetFloat(),
            Throttle = r.GetByte(),
            Rudder = r.GetSByte(),
            Anchor = (AnchorState)r.GetByte(),
            AnchorRaiseTicks = r.GetUShort(),
            PlunderIslandId = r.GetOptionalInt(),
            PlunderTicks = r.GetUShort(),
            Stance = (NpcStance)r.GetByte(),
        };
        if (r.GetBool())
            s.MoveTarget = r.GetVector2();
        for (var i = 0; i < s.Cooldowns.Length; i++)
        {
            var count = r.GetByte();
            var channels = new (int, int)[count];
            for (var c = 0; c < count; c++)
                channels[c] = (r.GetUShort(), r.GetUShort());
            s.Cooldowns[i] = channels;
        }
        return s;
    }
}
