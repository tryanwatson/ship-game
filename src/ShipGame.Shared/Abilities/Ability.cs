using System.Numerics;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Abilities;

/// <summary>Ability slots, bound to number keys 1-4.</summary>
public enum AbilitySlot
{
    One,
    Two,
    Three,
    Four,
}

/// <summary>
/// An immutable ability definition, shared by every ship that has it. Per-ship state such as
/// cooldowns lives in <see cref="AbilityState"/>.
/// </summary>
public abstract class Ability
{
    /// <summary>Stable identifier, used to name the ability over the network (see <see cref="AbilityRegistry"/>).</summary>
    public abstract string Id { get; }

    public abstract string Name { get; }

    /// <summary>One line for the shipyard and the loadout picker.</summary>
    public virtual string Description => "";

    /// <summary>Base cooldown, before skills and the ship's reload speed.</summary>
    public abstract int CooldownTicks { get; }

    /// <summary>This ship's cooldown for the ability: the base, changed by its skills (not yet by its reload speed).</summary>
    public float CooldownTicksFor(Ship ship) => ship.AbilityValue(Id, AbilityStat.Cooldown, CooldownTicks);

    /// <summary>
    /// Aimed at a point: the client shows a targeting indicator while the key is held and casts on release.
    /// Others fire the moment the key goes down.
    /// </summary>
    public virtual bool IsAimed => false;

    /// <summary>Independent cooldowns this ability has (1 for most; the broadside has one per side).</summary>
    public virtual int CooldownChannels => 1;

    /// <summary>Which cooldown channel a cast toward <paramref name="target"/> uses (and must find ready).</summary>
    public virtual int ChannelFor(Ship caster, Vector2 target) => 0;

    /// <summary>
    /// How long <paramref name="caster"/> takes to fire after deciding to: 0 fires at once. Otherwise the cast is a
    /// public <see cref="ShotWarning"/> until it goes off, so whoever's in the way can get out of it.
    /// </summary>
    public virtual int WindupTicksFor(Ship caster) => 0;

    /// <summary>
    /// Fires a shot laid <see cref="WindupTicksFor"/> ago, from where the ship is now. By default it's cast at the
    /// warning's target; abilities whose aim depends on how the ship lies (the broadside's side) keep what was shown.
    /// </summary>
    public virtual bool CastWarned(World world, Ship caster, ShotWarning warning) => Cast(world, caster, warning.Target);

    /// <summary>
    /// Performs the ability. <paramref name="target"/> is the cursor's world position at cast time;
    /// abilities that aren't targeted ignore it. Returns false if the cast was rejected (no cooldown is spent).
    /// </summary>
    public abstract bool Cast(World world, Ship caster, Vector2 target);
}

/// <summary>
/// A ship's copy of an ability: its cooldowns. Most abilities have one cooldown channel; some (the broadside's two
/// gun decks) have several that reload independently, and a cast uses the channel <see cref="Ability.ChannelFor"/> picks.
/// </summary>
public sealed class AbilityState
{
    private readonly int[] _remaining;
    private readonly int[] _duration;

    public AbilityState(Ability definition)
    {
        Definition = definition;
        _remaining = new int[Math.Max(1, definition.CooldownChannels)];
        _duration = new int[_remaining.Length];
    }

    public Ability Definition { get; }

    public int Channels => _remaining.Length;

    /// <summary>True if at least one channel can fire.</summary>
    public bool IsReady => _remaining.Any(r => r == 0);

    public bool IsChannelReady(int channel) => RemainingTicks(channel) == 0;

    public int RemainingTicks(int channel) => InRange(channel) ? _remaining[channel] : 0;

    /// <summary>Length of the channel's current cooldown, after the ship's cooldown speed was applied.</summary>
    public int DurationTicks(int channel) => InRange(channel) ? _duration[channel] : 0;

    /// <summary>1 right after casting, falling to 0 when the channel is ready.</summary>
    public float CooldownFraction(int channel) =>
        DurationTicks(channel) == 0 ? 0f : (float)RemainingTicks(channel) / DurationTicks(channel);

    // Single-channel shorthands (channel 0).
    public int CooldownRemainingTicks => RemainingTicks(0);
    public int CooldownDurationTicks => DurationTicks(0);

    /// <summary>Starts a channel's cooldown, shortened by <paramref name="cooldownSpeed"/> (1 = normal).</summary>
    public void StartCooldown(int channel, float cooldownSpeed) => StartCooldown(channel, Definition.CooldownTicks, cooldownSpeed);

    /// <summary>Starts a channel's cooldown of <paramref name="baseTicks"/>, shortened by <paramref name="cooldownSpeed"/>.</summary>
    public void StartCooldown(int channel, float baseTicks, float cooldownSpeed)
    {
        if (!InRange(channel))
            return;
        var duration = baseTicks / MathF.Max(cooldownSpeed, 0.01f);
        _duration[channel] = Math.Max(1, (int)MathF.Round(duration));
        _remaining[channel] = _duration[channel];
    }

    /// <summary>Sets a channel's cooldown directly, for a client mirroring the server's ship.</summary>
    public void Restore(int channel, int remainingTicks, int durationTicks)
    {
        if (!InRange(channel))
            return;
        _remaining[channel] = Math.Max(0, remainingTicks);
        _duration[channel] = Math.Max(0, durationTicks);
    }

    /// <summary>Takes <paramref name="fraction"/> of each reloading channel's full cooldown off what's left.</summary>
    public void Refund(float fraction)
    {
        for (var i = 0; i < _remaining.Length; i++)
            _remaining[i] = Math.Max(0, _remaining[i] - (int)MathF.Round(_duration[i] * fraction));
    }

    public void TickCooldown()
    {
        for (var i = 0; i < _remaining.Length; i++)
        {
            if (_remaining[i] > 0)
                _remaining[i]--;
        }
    }

    private bool InRange(int channel) => channel >= 0 && channel < _remaining.Length;
}
