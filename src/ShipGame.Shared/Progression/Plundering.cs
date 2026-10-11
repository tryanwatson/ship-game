using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Progression;

/// <summary>
/// Players plunder an island by riding at anchor close off its shore for <see cref="DurationSeconds"/>. Each
/// island can only be plundered once; after that it has nothing left to give. The gold is split evenly
/// between the plunderer and every other player afloat within <see cref="ShareRange"/> of the shore when it's taken.
/// </summary>
public static class Plundering
{
    /// <summary>How close (tiles from ship center to shore) a ship must anchor to plunder an island.</summary>
    public const float Range = 4f;

    /// <summary>How close (tiles from ship center to shore) another player must be to share in a plunder.</summary>
    public const float ShareRange = 12f;

    public const float DurationSeconds = 5f;

    public static readonly int DurationTicks = (int)(DurationSeconds * SimConstants.TickRate);

    public static void Step(World world)
    {
        foreach (var ship in world.Ships)
        {
            if (ship.OwnerPlayerId is not { } playerId)
                continue; // pirates at anchor are just waiting for you

            if (ship.Anchor != AnchorState.Down)
                ship.PlunderConsentIslandId = null;

            var island = ship.Anchor == AnchorState.Down ? PlunderableFrom(world, ship.Position) : null;

            // Shipyards are shops first: they're only plundered when the player picks that option.
            if (island is not null && world.IsPort(island) && ship.PlunderConsentIslandId != island.Id)
                island = null;

            if (island is null)
            {
                ship.PlunderIslandId = null;
                ship.PlunderTicks = 0;
                continue;
            }

            if (ship.PlunderIslandId != island.Id)
            {
                ship.PlunderIslandId = island.Id;
                ship.PlunderTicks = 0;
            }

            if (++ship.PlunderTicks < DurationTicks)
                continue;

            var gold = PlunderFor(world, island);
            GoldShares.Pay(world, gold, playerId, Nearby(world, island, playerId));
            world.MarkPlundered(island);
            world.Emit(new IslandPlundered(world.Tick, island.Id, playerId, gold));
            ship.PlunderConsentIslandId = null;
            ship.PlunderIslandId = null;
            ship.PlunderTicks = 0;
        }
    }

    /// <summary>
    /// What plundering <paramref name="island"/> pays, before it's shared: its gold, more for a bigger crew (by
    /// <see cref="Fortresses.CrewScale"/>), since the crew shares it and there are no more islands for more sailors.
    /// </summary>
    public static int PlunderFor(World world, Island island) =>
        (int)MathF.Round(island.PlunderGold * Fortresses.CrewScale(world.Players.Count));

    /// <summary>Players other than <paramref name="plundererId"/> close enough to <paramref name="island"/> to share its gold.</summary>
    public static IEnumerable<int> Nearby(World world, Island island, int plundererId) =>
        world.Ships
            .Where(s => s.OwnerPlayerId is { } id && id != plundererId && !s.IsSunk && island.DistanceTo(s.Position) <= ShareRange)
            .Select(s => s.OwnerPlayerId!.Value);

    /// <summary>0..1 progress of the ship's current plunder; 0 when it isn't plundering.</summary>
    public static float Progress(Ship ship) => ship.PlunderIslandId is null ? 0f : (float)ship.PlunderTicks / DurationTicks;

    /// <summary>
    /// The island a ship anchoring at <paramref name="position"/> would plunder: the nearest one within
    /// <see cref="Range"/> that hasn't been plundered already (or a fortress still held). Null if anchoring here would plunder nothing.
    /// </summary>
    public static Island? PlunderableFrom(World world, Vector2 position)
    {
        Island? nearest = null;
        var nearestDistance = Range;
        foreach (var island in world.Islands)
        {
            if (world.IsPlundered(island) || world.IsHeld(island))
                continue;
            if (Vector2.Distance(position, island.Center) - island.BoundingRadius > nearestDistance)
                continue;
            var distance = island.DistanceTo(position);
            if (distance <= nearestDistance)
            {
                nearest = island;
                nearestDistance = distance;
            }
        }
        return nearest;
    }
}
