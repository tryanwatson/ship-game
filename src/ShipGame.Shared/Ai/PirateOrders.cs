using System.Numerics;

namespace ShipGame.Shared.Ai;

/// <summary>What a pirate does with itself between fights. Handed out when it's put to sea.</summary>
public abstract record PirateOrders;

/// <summary>
/// Keeps to a patch of sea: cruises from one random spot to another between <paramref name="MinRadius"/> and
/// <paramref name="Radius"/> of <paramref name="Center"/> (going round it, when there's an island in the middle), and
/// treats anyone who comes within <paramref name="Watch"/> of the center as a trespasser, wherever on the patch the
/// pirate happens to be. A <paramref name="Radius"/> of 0 holds station at anchor on the center.
/// </summary>
/// <param name="IslandId">The island it guards, if any.</param>
public sealed record GuardPost(Vector2 Center, float Radius, float MinRadius = 0f, float Watch = 0f, int? IslandId = null) : PirateOrders;

/// <summary>Roams a band of sea running the map's width, sailing from one end of it to another.</summary>
/// <param name="North">World Y of the band's northern edge (north is -Y).</param>
/// <param name="South">World Y of its southern edge.</param>
public sealed record RoamOrders(float North, float South) : PirateOrders;
