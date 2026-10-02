using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Stats;

namespace ShipGame.Shared.Simulation;

/// <summary>
/// A ship in world space. World space is a flat 2D plane with Y pointing "down" (like screen
/// space), so a positive heading change turns the ship clockwise / to starboard.
/// </summary>
public enum Team
{
    Players,
    Pirates,
}

public sealed class Ship
{
    public const int AbilitySlotCount = 4;

    private readonly AbilityState?[] _abilities = new AbilityState?[AbilitySlotCount];

    public Ship(int id, int? ownerPlayerId, ShipStats stats, IReadOnlyList<Ability?>? abilities = null)
    {
        Id = id;
        OwnerPlayerId = ownerPlayerId;
        BaseStats = stats;
        Stats = stats;
        Health = stats.MaxHealth;
        Team = ownerPlayerId is null ? Team.Pirates : Team.Players;

        if (abilities is not null)
        {
            for (var i = 0; i < Math.Min(abilities.Count, AbilitySlotCount); i++)
                _abilities[i] = abilities[i] is { } ability ? new AbilityState(ability) : null;
        }
    }

    public int Id { get; }

    /// <summary>The controlling player, or null for NPC / uncontrolled ships.</summary>
    public int? OwnerPlayerId { get; }

    /// <summary>The hull's stats before upgrades.</summary>
    public ShipStats BaseStats { get; }

    /// <summary>Effective stats: <see cref="BaseStats"/> with <see cref="Modifiers"/> applied.</summary>
    public ShipStats Stats { get; private set; }

    /// <summary>Upgrades applied to this ship. Change through <see cref="AddModifier"/> / <see cref="RemoveModifiers"/>.</summary>
    public IReadOnlyList<StatModifier> Modifiers => _modifiers.All;

    private readonly StatModifiers _modifiers = new();

    public void AddModifier(StatModifier modifier)
    {
        _modifiers.Add(modifier);
        RecalculateStats();
    }

    /// <summary>Number of modifiers from <paramref name="source"/>, e.g. levels bought of an upgrade.</summary>
    public int ModifierCount(string source) => _modifiers.CountSource(source);

    public void RemoveModifiers(string source)
    {
        if (_modifiers.RemoveSource(source) > 0)
            RecalculateStats();
    }

    private void RecalculateStats()
    {
        var oldMaxHealth = Stats.MaxHealth;
        Stats = _modifiers.Apply(BaseStats);

        // Raising max health adds the same to current health, so an upgrade never shows as damage;
        // lowering it only clamps.
        var gained = Stats.MaxHealth - oldMaxHealth;
        Health = Math.Clamp(Health + MathF.Max(0f, gained), 0f, Stats.MaxHealth);
    }

    /// <summary>Whether the hull was against a shore last tick. Grounding only hurts on first contact.</summary>
    public bool IsAground { get; set; }

    /// <summary>Id of the ship that last damaged this one, for kill credit.</summary>
    public int? LastHitByShipId { get; set; }

    /// <summary>Ships only damage, and NPCs only hunt, ships of other teams.</summary>
    public Team Team { get; set; }

    public Vector2 Position { get; set; }

    /// <summary>Heading in radians; 0 faces +X.</summary>
    public float Heading { get; set; }

    /// <summary>Forward speed along <see cref="Heading"/>. Ships only move bow-first.</summary>
    public float Speed { get; set; }

    public Vector2? MoveTarget { get; set; }

    /// <summary>
    /// Helm state: the target can't be reached by turning yet, so the ship is running on until it can.
    /// See <see cref="ShipMovement.Step"/>.
    /// </summary>
    public bool IsHoldingCourse { get; set; }

    /// <summary>
    /// Sail setting from 0 (furled, stopped) to <see cref="ShipMovement.ThrottleLevels"/>. Sets cruise speed:
    /// with no move order the ship sails straight on at it.
    /// </summary>
    public int Throttle { get; set; }

    /// <summary>Sideways set from the wind, separate from <see cref="Speed"/> (which is always bow-first).</summary>
    public Vector2 WindDrift { get; set; }

    /// <summary>AI controller for NPC ships; null for player ships and inert hulks.</summary>
    public INpcBehavior? Behavior { get; set; }

    /// <summary>Anchor state; change it through <see cref="Anchoring"/>. Anchored ships can't move, turn, or drift.</summary>
    public AnchorState Anchor { get; set; }

    public int AnchorRaiseTicksRemaining { get; set; }

    /// <summary>
    /// True while the anchor is down or being raised. Setting it drops or weighs the anchor instantly, which
    /// NPCs and setup code use; players go through <see cref="Anchoring.Toggle"/> and its slow haul.
    /// </summary>
    public bool IsAnchored
    {
        get => Anchor != AnchorState.Weighed;
        set
        {
            if (value)
                Anchoring.Drop(this);
            else
                Anchoring.Weigh(this);
        }
    }

    /// <summary>The island this ship is part-way through plundering, if any.</summary>
    public int? PlunderIslandId { get; set; }

    public int PlunderTicks { get; set; }

    /// <summary>
    /// The shipyard island this player chose to plunder (rather than shop at) this anchorage. Shipyards only
    /// plunder on request; cleared when the anchor comes up.
    /// </summary>
    public int? PlunderConsentIslandId { get; set; }

    /// <summary>Manual helm: -1 port, 0 amidships, +1 starboard. Only steers when there's no move order.</summary>
    public int Rudder { get; set; }

    public float CruiseSpeed => Stats.MaxSpeed * Throttle / ShipMovement.ThrottleLevels;

    public float Health { get; set; }

    public bool IsSunk => Health <= 0f;

    public IReadOnlyList<AbilityState?> Abilities => _abilities;

    public AbilityState? GetAbility(AbilitySlot slot) => _abilities[(int)slot];

    // State at the start of the most recent tick, used to interpolate between ticks when rendering.
    public Vector2 PreviousPosition { get; set; }
    public float PreviousHeading { get; set; }

    public Vector2 Forward => new(MathF.Cos(Heading), MathF.Sin(Heading));

    /// <summary>Actual motion over the water: way made bow-first plus the wind's set.</summary>
    public Vector2 Velocity => Forward * Speed + WindDrift;
}
