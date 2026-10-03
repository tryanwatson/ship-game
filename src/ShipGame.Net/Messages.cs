using System.Numerics;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;

namespace ShipGame.Net;

/// <summary>Who's connected and who's ready, plus whether a run is underway (no joining mid-run).</summary>
public sealed record LobbyState(bool RunInProgress, IReadOnlyList<LobbyPlayer> Players, bool FriendlyFire = false);

public sealed record LobbyPlayer(int PlayerId, bool Ready);

/// <summary>A run is starting: clients rebuild their world. The islands come from the map, not the wire.</summary>
public sealed record RunStart(long Tick, Vector2 WorldSize, Vector2 Wind, bool FriendlyFire = false);

/// <summary>
/// Everything about a ship that rarely changes: identity, hull, guns, and upgrades. Sent reliably when the
/// ship appears and again whenever its stats change; snapshots carry the fast-moving rest. Stamped with the
/// server tick so clients apply it on the same timeline as events and snapshots.
/// </summary>
public sealed record ShipInfo(
    long Tick,
    int ShipId,
    int? OwnerPlayerId,
    Team Team,
    ShipStats BaseStats,
    IReadOnlyList<string?> AbilityIds,
    IReadOnlyList<StatModifier> Modifiers,
    Vector2 Position,
    float Heading);

/// <summary>A ship's fast-changing state at one tick.</summary>
public sealed class ShipState
{
    public int ShipId;
    public Vector2 Position;
    public float Heading;
    public float Speed;
    public float Health;
    public byte Throttle;
    public sbyte Rudder;
    public AnchorState Anchor;
    public int AnchorRaiseTicks;
    public int? PlunderIslandId;
    public int PlunderTicks;
    public NpcStance Stance;
    public Vector2? MoveTarget;
    /// <summary>Per ability slot, per cooldown channel: (remaining, duration) ticks. Empty for an empty slot.</summary>
    public (int Remaining, int Duration)[][] Cooldowns = new (int, int)[Ship.AbilitySlotCount][];
}

public sealed record PlayerSnapshot(int PlayerId, int Gold, int Kills, int RespawnTicks);

/// <summary>The world as of one server tick, as far as clients need to draw it.</summary>
public sealed class Snapshot
{
    public long Tick;
    public Vector2 Wind;
    public int Wave;
    public int TicksUntilNextWave;
    public bool RunOver;
    public List<PlayerSnapshot> Players = new();
    public List<(int IslandId, int Ticks)> IslandCooldowns = new();
    public List<ShipState> Ships = new();

    public ShipState? Find(int shipId) => Ships.Find(s => s.ShipId == shipId);

    public static Snapshot Capture(World world)
    {
        var snapshot = new Snapshot
        {
            Tick = world.Tick,
            Wind = world.Wind,
            Wave = world.Waves?.Wave ?? 0,
            TicksUntilNextWave = world.Waves?.TicksUntilNextWave ?? 0,
            RunOver = world.IsRunOver,
        };
        foreach (var player in world.Players.Values)
            snapshot.Players.Add(new PlayerSnapshot(player.PlayerId, player.Gold, player.Kills, player.RespawnTicksRemaining));
        foreach (var (islandId, ticks) in world.PlunderCooldowns)
            snapshot.IslandCooldowns.Add((islandId, ticks));
        foreach (var ship in world.Ships)
            snapshot.Ships.Add(CaptureShip(ship));
        return snapshot;
    }

    private static ShipState CaptureShip(Ship ship)
    {
        var state = new ShipState
        {
            ShipId = ship.Id,
            Position = ship.Position,
            Heading = ship.Heading,
            Speed = ship.Speed,
            Health = ship.Health,
            Throttle = (byte)ship.Throttle,
            Rudder = (sbyte)ship.Rudder,
            Anchor = ship.Anchor,
            AnchorRaiseTicks = ship.AnchorRaiseTicksRemaining,
            PlunderIslandId = ship.PlunderIslandId,
            PlunderTicks = ship.PlunderTicks,
            Stance = ship.Stance,
            MoveTarget = ship.MoveTarget,
        };
        for (var i = 0; i < Ship.AbilitySlotCount; i++)
        {
            var ability = ship.Abilities[i];
            state.Cooldowns[i] = ability is null
                ? Array.Empty<(int, int)>()
                : Enumerable.Range(0, ability.Channels).Select(c => (ability.RemainingTicks(c), ability.DurationTicks(c))).ToArray();
        }
        return state;
    }

    public static ShipInfo DescribeShip(Ship ship, long tick) => new(
        tick,
        ship.Id,
        ship.OwnerPlayerId,
        ship.Team,
        ship.BaseStats,
        ship.Abilities.Select(a => a?.Definition.Id).ToList(),
        ship.Modifiers.ToList(),
        ship.Position,
        ship.Heading);
}
