using ShipGame.Shared.Abilities;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>An aimed ability being held down: draw its targeting indicator toward <paramref name="Cursor"/> (world space).</summary>
public readonly record struct AimPreview(AbilitySlot Slot, NVector2 Cursor);
