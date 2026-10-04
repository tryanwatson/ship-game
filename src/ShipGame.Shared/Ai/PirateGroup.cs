using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Ai;

/// <summary>
/// Pirates that sail together: the first of them still afloat leads, and the rest keep station on it in a V. When
/// any of them is fighting, the others who are free join in on the same target.
/// </summary>
public sealed class PirateGroup
{
    // Station for the nth follower, relative to the leader: (astern, to starboard).
    private static readonly Vector2[] Slots =
    {
        new(-4f, 4f), new(-4f, -4f), new(-8f, 8f), new(-8f, -8f), new(-12f, 0f),
    };

    private readonly List<int> _memberIds = new();

    // Who the group is fighting, and when a member last said so.
    private int? _targetId;
    private long _reportTick = long.MinValue;

    public IReadOnlyList<int> MemberIds => _memberIds;

    public void Add(Ship ship) => _memberIds.Add(ship.Id);

    /// <summary>The first member still afloat.</summary>
    public Ship? Leader(World world)
    {
        foreach (var id in _memberIds)
        {
            if (world.FindShip(id) is { IsSunk: false } ship)
                return ship;
        }
        return null;
    }

    /// <summary>Where <paramref name="follower"/> should be, keeping station on <paramref name="leader"/>.</summary>
    public Vector2 StationFor(World world, Ship leader, Ship follower)
    {
        var index = -1;
        foreach (var id in _memberIds)
        {
            if (id == leader.Id || world.FindShip(id) is not { IsSunk: false })
                continue;
            index++;
            if (id == follower.Id)
                break;
        }
        var slot = Slots[Math.Clamp(index, 0, Slots.Length - 1)];
        var forward = new Vector2(MathF.Cos(leader.Heading), MathF.Sin(leader.Heading));
        var starboard = new Vector2(-forward.Y, forward.X);
        return leader.Position + forward * slot.X + starboard * slot.Y;
    }

    /// <summary>A member fighting <paramref name="target"/> calls the rest in. Hunters report every tick they fight.</summary>
    public void Report(Ship target, long tick)
    {
        _targetId = target.Id;
        _reportTick = tick;
    }

    /// <summary>Who the group is fighting right now, if anyone is.</summary>
    public Ship? Rallying(World world) =>
        world.Tick - _reportTick <= 1 && _targetId is { } id && world.FindShip(id) is { IsSunk: false } target ? target : null;
}
