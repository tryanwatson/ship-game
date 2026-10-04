using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Stats;
using ShipGame.Shared.Trading;
using ShipGame.Shared.Upgrades;

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

    /// <summary>Replaces every modifier at once, for a client mirroring the server's ship.</summary>
    public void ReplaceModifiers(IEnumerable<StatModifier> modifiers)
    {
        foreach (var source in _modifiers.All.Select(m => m.Source).Distinct().ToList())
            _modifiers.RemoveSource(source);
        foreach (var modifier in modifiers)
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

    /// <summary>
    /// Bumped whenever <see cref="Stats"/>, the guns, or the skills change, so replication can tell when to resend them.
    /// </summary>
    public int StatsVersion { get; private set; }

    private void RecalculateStats()
    {
        StatsVersion++;
        var oldMaxHealth = Stats.MaxHealth;
        Stats = _cardStats.Count == 0
            ? _modifiers.Apply(BaseStats)
            : StatModifiers.Apply(BaseStats, _modifiers.All.Concat(_cardStats).ToList());

        // Raising max health adds the same to current health, so an upgrade never shows as damage;
        // lowering it only clamps.
        var gained = Stats.MaxHealth - oldMaxHealth;
        Health = Math.Clamp(Health + MathF.Max(0f, gained), 0f, Stats.MaxHealth);
    }

    /// <summary>
    /// How dangerous a pirate is (see <see cref="Progression.PirateLevels"/>); 0 for player ships, which have no level.
    /// Shown beside the health bar. Change through <see cref="Progression.PirateLevels.Apply"/> in game code.
    /// </summary>
    public int Level
    {
        get => _level;
        set
        {
            if (_level == value)
                return;
            _level = value;
            StatsVersion++;
        }
    }

    private int _level;

    /// <summary>A boss: a pirate flagship sent after the crew as fortresses fall (see <c>RunDirector</c>).</summary>
    public bool IsBoss
    {
        get => _isBoss;
        set
        {
            if (_isBoss == value)
                return;
            _isBoss = value;
            StatsVersion++;
        }
    }

    private bool _isBoss;

    /// <summary>
    /// For a fort (a gun on a fortress's shore, see <c>Fortresses</c>): the island it stands on. Forts never move, aren't
    /// pushed about, and fire over their own island; null for ships.
    /// </summary>
    public int? FortIslandId
    {
        get => _fortIslandId;
        set
        {
            if (_fortIslandId == value)
                return;
            _fortIslandId = value;
            StatsVersion++;
        }
    }

    private int? _fortIslandId;

    public bool IsFort => _fortIslandId is not null;

    /// <summary>Whether the hull was against a shore last tick. Grounding only hurts on first contact.</summary>
    public bool IsAground { get; set; }

    /// <summary>Id of the ship that last damaged this one, for kill credit.</summary>
    public int? LastHitByShipId { get; set; }

    /// <summary>The tick a weapon last damaged this ship (see <see cref="LastHitByShipId"/> for whose); null if never.</summary>
    public long? LastHitTick { get; set; }

    private readonly Dictionary<int, long> _playerHits = new();

    /// <summary>The tick each player's weapons last damaged this ship, by player id, for sharing out the kill.</summary>
    public IReadOnlyDictionary<int, long> PlayerHits => _playerHits;

    public void RecordPlayerHit(int playerId, long tick) => _playerHits[playerId] = tick;

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
    /// with no move order the ship sails straight on at it. <see cref="ShipMovement.AsternThrottle"/> (-1) rows astern.
    /// </summary>
    public int Throttle { get; set; }

    /// <summary>Sideways set from the wind, separate from <see cref="Speed"/> (which is always bow-first).</summary>
    public Vector2 WindDrift { get; set; }

    /// <summary>AI controller for NPC ships; null for player ships and inert hulks. Server-side only.</summary>
    public INpcBehavior? Behavior { get; set; }

    /// <summary>The NPC's visible stance, kept up to date by its behavior. Replicated; read this, not <see cref="Behavior"/>.</summary>
    public NpcStance Stance { get; set; }

    /// <summary>Anchor state; change it through <see cref="Anchoring"/>. Anchored ships can't move, turn, or drift.</summary>
    public AnchorState Anchor { get; set; }

    public int AnchorRaiseTicksRemaining { get; set; }

    /// <summary>Ticks of holding the anchor key left before it lets go; 0 when it isn't being held.</summary>
    public int AnchorDropTicksRemaining { get; set; }

    /// <summary>
    /// True while the anchor is down or being raised. Setting it drops or weighs the anchor instantly, which
    /// NPCs and setup code use; players go through <see cref="Anchoring.PressKey"/>, its hold, and its slow haul.
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

    /// <summary>Speed under the current sail; 0 with the sails furled or rowing astern.</summary>
    public float CruiseSpeed => Stats.MaxSpeed * Math.Max(0, Throttle) / ShipMovement.ThrottleLevels;

    private readonly List<CargoLot> _cargo = new();

    /// <summary>Contract cargo in the hold. Change through <see cref="Contracts"/> in game code, so changes are announced.</summary>
    public IReadOnlyList<CargoLot> Cargo => _cargo;

    /// <summary>Whole units of cargo the hold takes (from <see cref="ShipStats.CargoCapacity"/>, so upgrades can raise it).</summary>
    public int CargoCapacity => (int)MathF.Floor(Stats.CargoCapacity + 1e-3f);

    public int CargoUsed => _cargo.Sum(c => c.RemainingUnits);

    public int FreeCargo => Math.Max(0, CargoCapacity - CargoUsed);

    public void LoadCargo(CargoLot lot) => _cargo.Add(lot);

    public bool UnloadCargo(int contractId) => _cargo.RemoveAll(c => c.Contract.Id == contractId) > 0;

    public void ClearCargo() => _cargo.Clear();

    public float Health { get; set; }

    public bool IsSunk => Health <= 0f;

    public IReadOnlyList<AbilityState?> Abilities => _abilities;

    public AbilityState? GetAbility(AbilitySlot slot) => _abilities[(int)slot];

    /// <summary>The ability with this <see cref="Ability.Id"/>, if the ship has it.</summary>
    public AbilityState? FindAbility(string abilityId) => _abilities.FirstOrDefault(a => a?.Definition.Id == abilityId);

    public bool HasAbility(string abilityId) => FindAbility(abilityId) is not null;

    /// <summary>The first empty slot, if any.</summary>
    public AbilitySlot? FreeAbilitySlot
    {
        get
        {
            var index = Array.IndexOf(_abilities, null);
            return index < 0 ? null : (AbilitySlot)index;
        }
    }

    /// <summary>
    /// Puts <paramref name="ability"/> in <paramref name="slot"/> (null empties it). A slot that already holds the same
    /// ability keeps its cooldowns.
    /// </summary>
    public void SetAbility(AbilitySlot slot, Ability? ability)
    {
        var index = (int)slot;
        if (_abilities[index]?.Definition == ability)
            return;
        _abilities[index] = ability is null ? null : new AbilityState(ability);
        StatsVersion++;
    }

    private readonly List<SkillDefinition> _skills = new();
    private readonly List<AbilityModifier> _abilityModifiers = new();

    /// <summary>Skills bought for this ship's weapons, in the order they were bought. Change through <see cref="AddSkill"/>.</summary>
    public IReadOnlyList<SkillDefinition> Skills => _skills;

    /// <summary>Every ability modifier the skills and cards grant, tagged with their sources.</summary>
    public IReadOnlyList<AbilityModifier> AbilityModifiers => _abilityModifiers;

    public bool HasSkill(string skillId) => _skills.Any(s => s.Id == skillId);

    public void AddSkill(SkillDefinition skill)
    {
        if (HasSkill(skill.Id))
            return;
        _skills.Add(skill);
        _abilityModifiers.AddRange(skill.Modifiers);
        StatsVersion++;
    }

    /// <summary>Replaces every skill at once, for a client mirroring the server's ship.</summary>
    public void ReplaceSkills(IEnumerable<SkillDefinition> skills)
    {
        _skills.Clear();
        _skills.AddRange(skills.DistinctBy(s => s.Id));
        RebuildAbilityModifiers();
        StatsVersion++;
    }

    private readonly List<CardPick> _cards = new();
    private readonly List<StatModifier> _cardStats = new();
    private readonly Dictionary<Perk, float> _perks = new();

    /// <summary>
    /// Cards held, in the order they were chosen: silver and gold may appear more than once (they stack); a prismatic
    /// appears once, at the level it's been improved to (see <see cref="CardStacking"/>).
    /// </summary>
    public IReadOnlyList<CardPick> Cards => _cards;

    /// <summary>Plays a card on this ship: its stats, weapon changes and perks apply from now on, on top of upgrades and skills.</summary>
    public void AddCard(CardPick card)
    {
        CardStacking.Add(_cards, card);
        OnCardsChanged();
    }

    /// <summary>Replaces every card at once: for a client mirroring the server's ship, and a ship taking on its player's hand.</summary>
    public void ReplaceCards(IEnumerable<CardPick> cards)
    {
        _cards.Clear();
        _cards.AddRange(cards);
        OnCardsChanged();
    }

    /// <summary>A perk's total from every card held; 0 without any.</summary>
    public float PerkValue(Perk perk) => _perks.GetValueOrDefault(perk);

    private void OnCardsChanged()
    {
        _cardStats.Clear();
        _perks.Clear();
        foreach (var card in _cards)
        {
            var definition = card.Definition;
            _cardStats.AddRange(definition.StatModifiersFor(card));
            foreach (var (perk, value) in definition.PerksFor(card))
                _perks[perk] = _perks.GetValueOrDefault(perk) + value;
        }
        RebuildAbilityModifiers();
        RecalculateStats();
    }

    private void RebuildAbilityModifiers()
    {
        _abilityModifiers.Clear();
        foreach (var skill in _skills)
            _abilityModifiers.AddRange(skill.Modifiers);
        foreach (var card in _cards)
            _abilityModifiers.AddRange(card.Definition.AbilityModifiersFor(card));
    }

    // ---- What cards do in a fight. Server-side, except Marked, which snapshots carry. ----------------------------

    /// <summary>Scales the damage of what the ship fires right now: below 1 while a card's echo fires.</summary>
    public float CastDamageScale { get; set; } = 1f;

    /// <summary>What's firing right now is an echo (it changes no reloads).</summary>
    public bool IsEchoing { get; set; }

    /// <summary>Until when it takes extra damage from everyone (Hunter's Mark), and how much.</summary>
    public long MarkedUntilTick { get; set; } = -1;

    public float MarkBonus { get; set; }

    /// <summary>Marked as a target right now: shown over its health bar.</summary>
    public bool IsMarked { get; set; }

    /// <summary>Until when it's slowed (Chain Shot): a speed modifier that comes off then.</summary>
    public long SlowedUntilTick { get; set; } = -1;

    /// <summary>When Second Wind can next save it.</summary>
    public long SecondWindReadyTick { get; set; }

    /// <summary>When its ram can next do damage (it hits once per contact, not every tick).</summary>
    public long RamReadyTick { get; set; }

    /// <summary>
    /// One of an ability's numbers on this ship: <paramref name="baseValue"/> with the ship's skills and cards for that
    /// ability applied, (base + flat) * (1 + percent) * multipliers like <see cref="StatModifiers"/>.
    /// </summary>
    public float AbilityValue(string abilityId, AbilityStat stat, float baseValue)
    {
        var flat = 0f;
        var percent = 0f;
        var multiplier = 1f;
        foreach (var modifier in _abilityModifiers)
        {
            if (modifier.Stat != stat || modifier.AbilityId != abilityId)
                continue;
            switch (modifier.Kind)
            {
                case ModifierKind.Flat:
                    flat += modifier.Value;
                    break;
                case ModifierKind.Percent:
                    percent += modifier.Value;
                    break;
                default:
                    multiplier *= modifier.Value;
                    break;
            }
        }
        return MathF.Max(0f, (baseValue + flat) * (1f + percent) * multiplier);
    }

    // State at the start of the most recent tick, used to interpolate between ticks when rendering.
    public Vector2 PreviousPosition { get; set; }
    public float PreviousHeading { get; set; }

    /// <summary>
    /// How far behind the server this ship's player saw everyone else when they last fired: their shots strike
    /// ships where they were that many ticks ago (see <see cref="World.MaxShotRewindTicks"/>). 0 for pirates.
    /// </summary>
    public int ShotRewindTicks { get; set; }

    // Where the ship was at each of the last few ticks, for shots fired by a lagging player. Stamped tick + 1, so 0 is empty.
    private readonly (long Stamp, Vector2 Position, float Heading)[] _poseHistory = new (long, Vector2, float)[World.MaxShotRewindTicks + 1];

    /// <summary>Notes the ship's pose as of <paramref name="tick"/>.</summary>
    public void RecordPose(long tick) => _poseHistory[tick % _poseHistory.Length] = (tick + 1, Position, Heading);

    /// <summary>The ship's pose as of <paramref name="tick"/>, if that's recent enough to remember; otherwise where it is now.</summary>
    public (Vector2 Position, float Heading) PoseAt(long tick)
    {
        if (tick >= 0)
        {
            var pose = _poseHistory[tick % _poseHistory.Length];
            if (pose.Stamp == tick + 1)
                return (pose.Position, pose.Heading);
        }
        return (Position, Heading);
    }

    public Vector2 Forward => new(MathF.Cos(Heading), MathF.Sin(Heading));

    /// <summary>Actual motion over the water: way made bow-first plus the wind's set.</summary>
    public Vector2 Velocity => Forward * Speed + WindDrift;
}
