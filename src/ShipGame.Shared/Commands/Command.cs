using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Upgrades;

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

/// <summary>At a shipyard: pay to repair the hull to full health (see <c>Shipyards.RepairCost</c>).</summary>
public sealed record PurchaseRepairCommand(int PlayerId) : Command(PlayerId);

/// <summary>At a shipyard: buy the next level of an upgrade from <c>UpgradeCatalog</c>.</summary>
public sealed record PurchaseUpgradeCommand(int PlayerId, string UpgradeId) : Command(PlayerId);

/// <summary>At a shipyard: buy a locked weapon from <c>WeaponCatalog</c>; it takes the next free ability slot.</summary>
public sealed record UnlockAbilityCommand(int PlayerId, string AbilityId) : Command(PlayerId);

/// <summary>At a shipyard: buy a skill from one of the ship's weapons' trees (see <c>SkillTrees</c>).</summary>
public sealed record PurchaseSkillCommand(int PlayerId, string SkillId) : Command(PlayerId);

/// <summary>At a trading post (shipyard): buy one of the contracts it has on offer, by <c>TradeContract.Id</c>.</summary>
public sealed record PurchaseContractCommand(int PlayerId, int ContractId) : Command(PlayerId);

/// <summary>
/// Sets the helm: -1 hard to port, 0 amidships, +1 hard to starboard. Sent when the input changes rather than
/// every frame. Putting the helm over takes manual control, cancelling any move order.
/// </summary>
public sealed record SetRudderCommand(int PlayerId, int Rudder) : Command(PlayerId);

/// <summary>Raises or lowers the sail setting by <paramref name="Delta"/> levels; one below furled rows astern.</summary>
public sealed record AdjustThrottleCommand(int PlayerId, int Delta) : Command(PlayerId);

/// <summary>
/// Chooses <paramref name="CardId"/> from the oldest card offer waiting for the player (see <c>CardRewards</c>). Works
/// whether or not the player is afloat: cards belong to the player.
/// </summary>
public sealed record ChooseCardCommand(int PlayerId, string CardId) : Command(PlayerId);

/// <summary>
/// At the start of a run, once the starting card is chosen: the weapon to set sail with, from <c>WeaponCatalog</c>.
/// It goes on slot 1; the others are bought at ports as usual.
/// </summary>
public sealed record ChooseStartingWeaponCommand(int PlayerId, string AbilityId) : Command(PlayerId);

/// <summary>Pays gold to swap the cards in the player's oldest offer for a fresh draw (see <c>CardRewards.TryReroll</c>).</summary>
/// <param name="Tier">For a testing hand: the tier to deal it in. Ignored otherwise.</param>
public sealed record RerollCardsCommand(int PlayerId, CardTier? Tier = null) : Command(PlayerId);

/// <summary>
/// Casts the ability in <paramref name="Slot"/>. <paramref name="Target"/> is the cursor's world position.
/// <paramref name="ViewTick"/>, online, is the server tick the player was seeing other ships at when they fired: their
/// shots strike ships where they saw them (see <see cref="Simulation.Ship.ShotRewindTicks"/>). Null in-process.
/// </summary>
public sealed record CastAbilityCommand(int PlayerId, AbilitySlot Slot, Vector2 Target, long? ViewTick = null) : Command(PlayerId);
