using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Net;

/// <summary>
/// Which ships the server sends. The map is big and its pirates are spread all over it, so only ships near some player
/// are worth sending: everyone's own ships always, and pirates within <see cref="EnterRange"/> of a player ship
/// (comfortably beyond what any screen shows). A shown ship stays shown until it's beyond <see cref="LeaveRange"/>,
/// so one hovering at the edge doesn't flicker in and out. Every client gets the same set, so crewmates far apart
/// each see their own surroundings.
/// </summary>
public sealed class Relevance
{
    public const float EnterRange = 50f;
    public const float LeaveRange = 60f;

    private readonly HashSet<int> _shown = new();

    public bool IsShown(int shipId) => _shown.Contains(shipId);

    public void Clear() => _shown.Clear();

    /// <summary>
    /// Re-decides every ship: <paramref name="entered"/> gets the ships that have just come into range, and
    /// <paramref name="left"/> the ids of those that have just gone out of it. Ships that are gone from the world
    /// (sunk, or their player left) are forgotten without being reported.
    /// </summary>
    public void Update(World world, List<Ship> entered, List<int> left)
    {
        var players = new List<Vector2>();
        foreach (var ship in world.Ships)
        {
            if (ship.OwnerPlayerId is not null)
                players.Add(ship.Position);
        }

        _shown.RemoveWhere(id => world.FindShip(id) is null);
        foreach (var ship in world.Ships)
        {
            var shown = _shown.Contains(ship.Id);
            var relevant = ship.OwnerPlayerId is not null || IsNearAny(ship.Position, players, shown ? LeaveRange : EnterRange);
            if (relevant && !shown)
            {
                _shown.Add(ship.Id);
                entered.Add(ship);
            }
            else if (!relevant && shown)
            {
                _shown.Remove(ship.Id);
                left.Add(ship.Id);
            }
        }
    }

    private static bool IsNearAny(Vector2 position, List<Vector2> points, float range)
    {
        var rangeSquared = range * range;
        foreach (var point in points)
        {
            if (Vector2.DistanceSquared(position, point) <= rangeSquared)
                return true;
        }
        return false;
    }
}
