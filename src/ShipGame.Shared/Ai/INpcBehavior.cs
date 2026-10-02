using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Ai;

/// <summary>
/// Drives an NPC ship. Runs inside <see cref="World.Step"/> after player commands, so on a dedicated server
/// it's authoritative like everything else. Behaviors steer through the same helm (move target, sails,
/// rudder) as players, so NPCs handle exactly like player ships.
/// </summary>
public interface INpcBehavior
{
    void Update(World world, Ship ship);
}
