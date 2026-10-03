using System.Numerics;
using ShipGame.Shared.Abilities;

namespace ShipGame.Shared.Commands;

/// <summary>
/// A player intent. Clients never mutate the simulation directly; they send commands,
/// which become network messages once multiplayer lands.
/// </summary>
public abstract record Command(int PlayerId);

public sealed record MoveCommand(int PlayerId, Vector2 Target) : Command(PlayerId);

/// <summary>All stop: cancels any move order and furls the sails.</summary>
public sealed record StopCommand(int PlayerId) : Command(PlayerId);

/// <summary>
/// The anchor key went down or up. Pressed with the anchor up starts letting it go, which takes holding the key
/// for <c>Anchoring.DropSeconds</c> (releasing early cancels); pressed with it down starts the slow haul to raise it.
/// Sent on change, like the helm.
/// </summary>
public sealed record AnchorKeyCommand(int PlayerId, bool Pressed) : Command(PlayerId);

/// <summary>At a shipyard: plunder the island instead of trading (shipyards don't plunder unless asked).</summary>
public sealed record ChoosePlunderCommand(int PlayerId) : Command(PlayerId);

/// <summary>At a shipyard: buy the next level of an upgrade from <c>UpgradeCatalog</c>.</summary>
public sealed record PurchaseUpgradeCommand(int PlayerId, string UpgradeId) : Command(PlayerId);

/// <summary>
/// Sets the helm: -1 hard to port, 0 amidships, +1 hard to starboard. Sent when the input changes rather than
/// every frame. Putting the helm over takes manual control, cancelling any move order.
/// </summary>
public sealed record SetRudderCommand(int PlayerId, int Rudder) : Command(PlayerId);

/// <summary>Raises or lowers the sail setting by <paramref name="Delta"/> levels.</summary>
public sealed record AdjustThrottleCommand(int PlayerId, int Delta) : Command(PlayerId);

/// <summary>Casts the ability in <paramref name="Slot"/>. <paramref name="Target"/> is the cursor's world position.</summary>
public sealed record CastAbilityCommand(int PlayerId, AbilitySlot Slot, Vector2 Target) : Command(PlayerId);
