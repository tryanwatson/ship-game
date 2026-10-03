using System.Collections;
using System.Numerics;

namespace ShipGame.Shared.Simulation;

/// <summary>
/// What each team has seen of the map, in <see cref="CellSize"/>-tile cells. A cell is discovered once it comes
/// within <see cref="SightContains">sight</see> of one of the team's ships; discoveries are shared by the whole team
/// and last for the run. Islands count as discovered once any part of them is.
/// </summary>
public sealed class Discovery
{
    /// <summary>Tiles per cell side.</summary>
    public const float CellSize = 4f;

    // Sight is exactly what a player sees at the camera's maximum zoom-out on the reference window (2560 x 1440 iso
    // units: 1280 x 720 at zoom 0.5, see the client's Camera), centred on the ship. With 64 x 32 iso tiles, screen x
    // is (dx - dy) * 32 and screen y is (dx + dy) * 16, so the half-extents in world terms are:
    /// <summary>Half the sight extent along screen-x, as |dx - dy| in tiles (1280 / 32).</summary>
    public const float SightHalfAcross = 40f;

    /// <summary>Half the sight extent along screen-y, as |dx + dy| in tiles (720 / 16).</summary>
    public const float SightHalfUpDown = 45f;

    private readonly Dictionary<Team, BitArray> _discovered = new();

    public Discovery(Vector2 worldSize)
    {
        Columns = (int)MathF.Ceiling(worldSize.X / CellSize);
        Rows = (int)MathF.Ceiling(worldSize.Y / CellSize);
    }

    public int Columns { get; }

    public int Rows { get; }

    public int CellCount => Columns * Rows;

    /// <summary>Whether a point offset (dx, dy) tiles from a ship is within its sight.</summary>
    public static bool SightContains(Vector2 offset) =>
        MathF.Abs(offset.X - offset.Y) <= SightHalfAcross && MathF.Abs(offset.X + offset.Y) <= SightHalfUpDown;

    public int CellIndex(int column, int row) => row * Columns + column;

    public (int Column, int Row) CellOf(int index) => (index % Columns, index / Columns);

    public Vector2 CellCenter(int index)
    {
        var (column, row) = CellOf(index);
        return new Vector2((column + 0.5f) * CellSize, (row + 0.5f) * CellSize);
    }

    public bool IsDiscovered(Team team, int index) =>
        index >= 0 && index < CellCount && _discovered.TryGetValue(team, out var bits) && bits[index];

    public bool IsDiscovered(Team team, Vector2 position) => CellAt(position) is { } index && IsDiscovered(team, index);

    /// <summary>The cell containing <paramref name="position"/>, or null off the map.</summary>
    public int? CellAt(Vector2 position)
    {
        var column = (int)MathF.Floor(position.X / CellSize);
        var row = (int)MathF.Floor(position.Y / CellSize);
        return column >= 0 && column < Columns && row >= 0 && row < Rows ? CellIndex(column, row) : null;
    }

    /// <summary>An island is discovered once its center or any point of its outline lies in a discovered cell.</summary>
    public bool IsDiscovered(Team team, Island island)
    {
        if (IsDiscovered(team, island.Center))
            return true;
        foreach (var point in island.Outline)
        {
            if (IsDiscovered(team, point))
                return true;
        }
        return false;
    }

    public int DiscoveredCount(Team team) =>
        _discovered.TryGetValue(team, out var bits) ? bits.Cast<bool>().Count(b => b) : 0;

    /// <summary>Marks every cell whose center is within sight of <paramref name="viewer"/>. Returns the newly discovered cells.</summary>
    public List<int> RevealAround(Team team, Vector2 viewer)
    {
        var revealed = new List<int>();
        var bits = BitsFor(team);

        // Sight is a rotated rectangle; scan its bounding box and test each cell center.
        var reach = MathF.Max(SightHalfAcross, SightHalfUpDown);
        var minColumn = Math.Max(0, (int)MathF.Floor((viewer.X - reach) / CellSize));
        var maxColumn = Math.Min(Columns - 1, (int)MathF.Floor((viewer.X + reach) / CellSize));
        var minRow = Math.Max(0, (int)MathF.Floor((viewer.Y - reach) / CellSize));
        var maxRow = Math.Min(Rows - 1, (int)MathF.Floor((viewer.Y + reach) / CellSize));

        for (var row = minRow; row <= maxRow; row++)
        {
            for (var column = minColumn; column <= maxColumn; column++)
            {
                var index = CellIndex(column, row);
                if (bits[index] || !SightContains(CellCenter(index) - viewer))
                    continue;
                bits[index] = true;
                revealed.Add(index);
            }
        }
        return revealed;
    }

    /// <summary>Marks specific cells discovered, for a client mirroring the server.</summary>
    public void Reveal(Team team, IEnumerable<int> cells)
    {
        var bits = BitsFor(team);
        foreach (var index in cells)
        {
            if (index >= 0 && index < CellCount)
                bits[index] = true;
        }
    }

    private BitArray BitsFor(Team team)
    {
        if (!_discovered.TryGetValue(team, out var bits))
            _discovered[team] = bits = new BitArray(CellCount);
        return bits;
    }
}
