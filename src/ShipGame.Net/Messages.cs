using System.Numerics;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;

namespace ShipGame.Net;

/// <summary>Who's connected and who's ready, plus whether a run is underway (no joining mid-run).</summary>
/// <param name="StartingGold">Gold everyone starts the next run with; any player in the lobby can set it (for playtesting).</param>
public sealed record LobbyState(bool RunInProgress, IReadOnlyList<LobbyPlayer> Players, bool FriendlyFire = false, int StartingGold = 0);

/// <param name="StartingWeaponId">The weapon they've chosen to start the run with; null until they choose.</param>
public sealed record LobbyPlayer(int PlayerId, bool Ready, string? StartingWeaponId = null);

/// <summary>A run is starting: clients rebuild their world. The islands come from the map, not the wire.</summary>
public sealed record RunStart(long Tick, Vector2 WorldSize, Vector2 Wind, bool FriendlyFire = false);

/// <summary>
/// Everything about a ship that rarely changes: identity, hull, guns, skills, and upgrades. Sent reliably when the
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
    float Heading,
    IReadOnlyList<string>? SkillIds = null,
    int Level = 0,
    bool IsBoss = false);

/// <summary>A ship's fast-changing state at one tick.</summary>
public sealed class ShipState
{
    public int ShipId;
    public Vector2 Position;
    public float Heading;
    public float Speed;
    public float Health;
    public sbyte Throttle;
    public sbyte Rudder;
    public AnchorState Anchor;
    public int AnchorRaiseTicks;
    public int AnchorDropTicks;
    public int? PlunderIslandId;
    public int PlunderTicks;
    public NpcStance Stance;
    public Vector2? MoveTarget;
    // The rest of the movement state, so a client predicting its own ship can carry on from exactly here.
    public bool IsHoldingCourse;
    public Vector2 WindDrift;
    /// <summary>Per ability slot, per cooldown channel: (remaining, duration) ticks. Empty for an empty slot.</summary>
    public (int Remaining, int Duration)[][] Cooldowns = new (int, int)[Ship.AbilitySlotCount][];
}

public sealed record PlayerSnapshot(int PlayerId, int Gold, int Kills, int RespawnTicks);

/// <summary>The world as of one server tick, as far as clients need to draw it.</summary>
public sealed class Snapshot
{
    public long Tick;
    public Vector2 Wind;
    public RunStatus Run;
    public bool RunOver;
    public bool Victory;
    public List<PlayerSnapshot> Players = new();
    public List<(int IslandId, int Ticks)> IslandCooldowns = new();
    public List<ShipState> Ships = new();

    /// <summary>Per player: the sequence number of the last command the server had applied by this tick.</summary>
    public List<(int PlayerId, uint Sequence)> CommandAcks = new();

    public ShipState? Find(int shipId) => Ships.Find(s => s.ShipId == shipId);

    public uint AckFor(int playerId)
    {
        foreach (var (id, sequence) in CommandAcks)
        {
            if (id == playerId)
                return sequence;
        }
        return 0;
    }

    /// <param name="include">Which ships to send (by default, all of them): the server leaves out those nobody is near.</param>
    public static Snapshot Capture(World world, Func<Ship, bool>? include = null)
    {
        var snapshot = new Snapshot
        {
            Tick = world.Tick,
            Wind = world.Wind,
            Run = world.Director?.Status ?? default,
            RunOver = world.IsRunOver,
            Victory = world.IsVictory,
        };
        foreach (var player in world.Players.Values)
            snapshot.Players.Add(new PlayerSnapshot(player.PlayerId, player.Gold, player.Kills, player.RespawnTicksRemaining));
        foreach (var (islandId, ticks) in world.PlunderCooldowns)
            snapshot.IslandCooldowns.Add((islandId, ticks));
        foreach (var ship in world.Ships)
        {
            if (include is null || include(ship))
                snapshot.Ships.Add(CaptureShip(ship));
        }
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
            Throttle = (sbyte)ship.Throttle,
            Rudder = (sbyte)ship.Rudder,
            Anchor = ship.Anchor,
            AnchorRaiseTicks = ship.AnchorRaiseTicksRemaining,
            AnchorDropTicks = ship.AnchorDropTicksRemaining,
            PlunderIslandId = ship.PlunderIslandId,
            PlunderTicks = ship.PlunderTicks,
            Stance = ship.Stance,
            MoveTarget = ship.MoveTarget,
            IsHoldingCourse = ship.IsHoldingCourse,
            WindDrift = ship.WindDrift,
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
        ship.Heading,
        ship.Skills.Select(s => s.Id).ToList(),
        ship.Level,
        ship.IsBoss);
}
