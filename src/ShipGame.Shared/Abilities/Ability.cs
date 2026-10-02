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
    public abstract string Name { get; }

    public abstract int CooldownTicks { get; }

    /// <summary>
    /// Performs the ability. <paramref name="target"/> is the cursor's world position at cast time;
    /// abilities that aren't targeted ignore it. Returns false if the cast was rejected (no cooldown is spent).
    /// </summary>
    public abstract bool Cast(World world, Ship caster, Vector2 target);
}

public sealed class AbilityState
{
    public AbilityState(Ability definition)
    {
        Definition = definition;
    }

    public Ability Definition { get; }

    public int CooldownRemainingTicks { get; private set; }

    /// <summary>Length of the current cooldown, after the ship's cooldown speed was applied.</summary>
    public int CooldownDurationTicks { get; private set; }

    public bool IsReady => CooldownRemainingTicks == 0;

    /// <summary>1 right after casting, falling to 0 when ready.</summary>
    public float CooldownFraction =>
        CooldownDurationTicks == 0 ? 0f : (float)CooldownRemainingTicks / CooldownDurationTicks;

    /// <summary>Starts the cooldown, shortened by <paramref name="cooldownSpeed"/> (1 = normal).</summary>
    public void StartCooldown(float cooldownSpeed)
    {
        var duration = Definition.CooldownTicks / MathF.Max(cooldownSpeed, 0.01f);
        CooldownDurationTicks = Math.Max(1, (int)MathF.Round(duration));
        CooldownRemainingTicks = CooldownDurationTicks;
    }

    public void TickCooldown()
    {
        if (CooldownRemainingTicks > 0)
            CooldownRemainingTicks--;
    }
}
