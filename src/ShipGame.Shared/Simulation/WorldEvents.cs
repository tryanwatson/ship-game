using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Simulation;

/// <summary>
/// Something that happened in the world, for whoever needs to hear about it: the server forwards these to
/// clients (reliably), and the client can drive effects and sounds from them. Continuous state (positions,
/// health, anchor state) travels in snapshots instead; events cover discrete, one-off changes.
/// </summary>
public abstract record WorldEvent(long Tick);

public sealed record ShipSpawned(long Tick, int ShipId) : WorldEvent(Tick);

/// <param name="KillerShipId">Ship that landed the last hit, if any (it may have sunk too).</param>
public sealed record ShipSunk(long Tick, int ShipId, int? KillerShipId) : WorldEvent(Tick);

/// <summary>Everything a client needs to fly the ball itself: it moves in a straight line until it expires or the server reports an impact.</summary>
public sealed record ProjectileSpawned(
    long Tick, int ProjectileId, int OwnerShipId, Team Team, Vector2 Position, Vector2 Velocity, float Damage, int LifetimeTicks,
    float Radius = Projectile.DefaultRadius)
    : WorldEvent(Tick);

/// <summary>A shell is in the air: everyone can see where (<paramref name="Target"/>) and when (<paramref name="ImpactTick"/>) it lands.</summary>
public sealed record AreaStrikeLaunched(
    long Tick, int StrikeId, int OwnerShipId, Team Team, Vector2 Origin, Vector2 Target, float Radius, float Damage, long ImpactTick)
    : WorldEvent(Tick);

/// <summary>
/// A ship has laid a gun on <paramref name="Target"/> and fires at <paramref name="FireTick"/> (see <see cref="ShotWarning"/>):
/// everyone can see where it's pointed. The shot itself arrives as usual when it fires, with its <see cref="AbilityCast"/>.
/// </summary>
/// <param name="Channel">The cooldown channel it was laid with (the broadside's side; 0 for most).</param>
public sealed record ShotWarned(long Tick, int ShipId, AbilitySlot Slot, Vector2 Target, long FireTick, int Channel = 0) : WorldEvent(Tick);

/// <summary>Map cells a team has just discovered (see <see cref="Discovery"/>).</summary>
public sealed record AreaDiscovered(long Tick, Team Team, IReadOnlyList<int> Cells) : WorldEvent(Tick)
{
    // Records compare lists by reference; compare the cells themselves.
    public bool Equals(AreaDiscovered? other) =>
        other is not null && Tick == other.Tick && Team == other.Team && Cells.SequenceEqual(other.Cells);

    public override int GetHashCode() => HashCode.Combine(Tick, Team, Cells.Count);
}

/// <summary>A shell burst. Damage arrives via ship health; this is for the explosion.</summary>
public sealed record AreaStrikeImpact(long Tick, int StrikeId, Vector2 Target, float Radius) : WorldEvent(Tick);

/// <param name="ShipId">The ship it struck, or null if it hit land.</param>
/// <param name="PassedThrough">A piercing shot that carries on past the ship it struck.</param>
public sealed record ProjectileImpact(long Tick, int ProjectileId, int? ShipId, bool PassedThrough = false) : WorldEvent(Tick);

/// <param name="Channel">Which of the ability's cooldowns it used (the broadside's side; 0 for most).</param>
public sealed record AbilityCast(long Tick, int ShipId, AbilitySlot Slot, int CooldownTicks, int Channel = 0) : WorldEvent(Tick);

public sealed record ShipGrounded(long Tick, int ShipId) : WorldEvent(Tick);

public sealed record GoldChanged(long Tick, int PlayerId, int Gold, int Delta) : WorldEvent(Tick);

public sealed record IslandPlundered(long Tick, int IslandId, int PlayerId, int Gold) : WorldEvent(Tick);

public sealed record UpgradePurchased(long Tick, int ShipId, string UpgradeId, int Level) : WorldEvent(Tick);

/// <summary>A ship bought (or started the run with) a weapon; it's on <paramref name="Slot"/>'s key from now on.</summary>
public sealed record AbilityUnlocked(long Tick, int ShipId, string AbilityId, AbilitySlot Slot) : WorldEvent(Tick);

/// <summary>A ship bought a skill from one of its weapons' trees.</summary>
public sealed record SkillPurchased(long Tick, int ShipId, string SkillId) : WorldEvent(Tick);

/// <summary>A player's ship went down; they'll be back in <paramref name="RespawnTicks"/> unless the run ends first.</summary>
public sealed record PlayerSunk(long Tick, int PlayerId, int RespawnTicks) : WorldEvent(Tick);

public sealed record PlayerRespawned(long Tick, int PlayerId, int ShipId) : WorldEvent(Tick);

/// <summary>
/// The run is over: every player was sunk at once, or (<paramref name="Victory"/>) the last boss was.
/// </summary>
public sealed record RunEnded(long Tick, bool Victory = false) : WorldEvent(Tick);

/// <summary>A fortress's last gun fell: the island is taken, and every player is offered cards.</summary>
public sealed record FortressTaken(long Tick, int IslandId) : WorldEvent(Tick);

/// <summary>A player may choose one of <paramref name="CardIds"/> (see <c>CardRewards</c>); it joins the back of their queue.</summary>
public sealed record CardsOffered(long Tick, int PlayerId, CardOffer Offer) : WorldEvent(Tick);

/// <summary>
/// A player rerolled their oldest offer (free, or for gold): it's now <paramref name="Offer"/>, and they've paid for
/// <paramref name="Rerolls"/> rerolls this run.
/// </summary>
public sealed record CardsRerolled(long Tick, int PlayerId, CardOffer Offer, int Rerolls) : WorldEvent(Tick);

/// <summary>A player chose the weapon they set sail with; the run starts once everyone has.</summary>
public sealed record StartingWeaponChosen(long Tick, int PlayerId, string AbilityId) : WorldEvent(Tick);

/// <summary>A player chose <paramref name="Card"/> from the oldest offer in their queue, which is now gone.</summary>
public sealed record CardChosen(long Tick, int PlayerId, CardPick Card) : WorldEvent(Tick);

/// <summary>A ship with a ram ran into another and did it damage.</summary>
public sealed record ShipRammed(long Tick, int RammerShipId, int TargetShipId) : WorldEvent(Tick);

/// <summary>A shell set the water burning (Firestorm): a public patch to keep out of until <paramref name="EndTick"/>.</summary>
public sealed record FireStarted(long Tick, int FireId, int OwnerShipId, Team Team, Vector2 Position, float Radius, float Dps, long EndTick)
    : WorldEvent(Tick);

/// <summary>Boss number <paramref name="Round"/> (from 1) has come for the crew, starting near <paramref name="PreyPlayerId"/>.</summary>
public sealed record BossSpawned(long Tick, int ShipId, int Round, int PreyPlayerId) : WorldEvent(Tick);

/// <summary>A boss was worn down to the end of a phase (<paramref name="Phase"/> of them now passed): it calls escorts and lets fly.</summary>
public sealed record BossPhaseChanged(long Tick, int ShipId, int Phase) : WorldEvent(Tick);

/// <summary>Pirates have come to relieve a fortress under siege: <paramref name="Ships"/> of them, from the edge of the sea.</summary>
public sealed record ReliefFleetSighted(long Tick, int Ships) : WorldEvent(Tick);

/// <summary>The run's chart, drawn as it starts (see <see cref="SeaChart"/>).</summary>
public sealed record VoyageCharted(long Tick, SeaChart Chart) : WorldEvent(Tick)
{
    // The chart is a class: compare its stops.
    public bool Equals(VoyageCharted? other) => other is not null && Tick == other.Tick && Chart.Nodes.SequenceEqual(other.Chart.Nodes);

    public override int GetHashCode() => HashCode.Combine(Tick, Chart.Nodes.Count);
}

/// <summary>
/// The crew sailed into the region for chart stop <paramref name="NodeId"/> (see <see cref="World.LoadRegion"/>): a sea of
/// <paramref name="Size"/> with these islands. Everything but the players' ships was cleared away first: every entity
/// numbered below <paramref name="FirstEntityId"/> that no player owns is from the old region.
/// </summary>
public sealed record RegionEntered(long Tick, int NodeId, Vector2 Size, IReadOnlyList<Island> Islands, int FirstEntityId) : WorldEvent(Tick)
{
    // Records compare lists by reference; compare the islands by what they are.
    public bool Equals(RegionEntered? other) =>
        other is not null && Tick == other.Tick && NodeId == other.NodeId && Size == other.Size && FirstEntityId == other.FirstEntityId
        && Islands.Count == other.Islands.Count
        && Islands.Zip(other.Islands).All(pair => pair.First.Id == pair.Second.Id && pair.First.Outline.SequenceEqual(pair.Second.Outline));

    public override int GetHashCode() => HashCode.Combine(Tick, NodeId, Size, Islands.Count, FirstEntityId);
}

/// <summary>
/// A ship left play without sinking: raised by the server, per client, when a ship sails out of everyone's range and
/// stops being sent, though it's still afloat. The client drops it until it comes back into range.
/// </summary>
public sealed record ShipHidden(long Tick, int ShipId) : WorldEvent(Tick);

/// <summary>A player's command was refused; the server tells that player why.</summary>
public sealed record CommandRejected(long Tick, int PlayerId, Command Command, RejectionReason Reason) : WorldEvent(Tick);

public enum RejectionReason
{
    /// <summary>The player has no ship afloat.</summary>
    NoShip,

    /// <summary>Can't sail while the anchor is down or being raised.</summary>
    Anchored,

    /// <summary>The anchor is already being raised.</summary>
    AnchorBusy,

    InvalidSlot,
    EmptySlot,
    OnCooldown,
    CastFailed,

    NotAtShipyard,
    IslandAlreadyPlundered,
    UnknownUpgrade,
    MaxLevel,
    NotEnoughGold,

    /// <summary>The weapon or skill is already owned.</summary>
    AlreadyOwned,

    /// <summary>Every ability slot is taken.</summary>
    NoFreeSlot,

    /// <summary>A skill for a weapon the ship hasn't unlocked.</summary>
    AbilityLocked,

    /// <summary>The skill builds on one the ship doesn't have yet.</summary>
    MissingPrerequisite,

    /// <summary>The skill is on a branch closed off by a choice already made.</summary>
    ExcludedByChoice,

    /// <summary>This shipyard doesn't sell that upgrade's next level; a port deeper into the voyage does.</summary>
    NotStockedHere,

    /// <summary>The hull is already at full health: there's nothing to repair.</summary>
    NothingToRepair,

    /// <summary>That card isn't in the player's oldest offer (or they have none waiting).</summary>
    NoCardOffer,

    /// <summary>The game is paused while cards are chosen: no firing.</summary>
    Paused,

    /// <summary>The starting weapon comes after the starting card.</summary>
    ChooseCardFirst,

    /// <summary>Not at the start of a run, or the starting weapon's already chosen.</summary>
    NotChoosingWeapon,

    /// <summary>No such weapon.</summary>
    UnknownWeapon,

    /// <summary>The crew can't choose where to sail next until they're done here (the fortress taken, the boss sunk).</summary>
    NotChartingCourse,

    /// <summary>The chart doesn't lead there from here.</summary>
    UnknownCourse,
}
