using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Ai;

/// <summary>
/// A fort on a fortress's shore (see <c>Fortresses</c>). It can't move: it turns its gun toward the nearest enemy
/// within reach and fires whenever a shot would land, leading the target like a pirate ship does. Like a pirate ship's,
/// a battery's shot is laid before it fires (see <see cref="ShotWarning"/>), and the gun holds on that line until it goes
/// off. A battery's shot flies out over its own island but not through any other land; a mortar's shell flies over anything.
/// </summary>
public sealed class FortBehavior : INpcBehavior
{
    /// <summary>How fast the gun swings round to bear, in radians per second.</summary>
    public const float TraverseRate = MathF.PI / 2f;

    /// <summary>The enemy it's firing on, if any.</summary>
    public Ship? Target { get; private set; }

    public void Update(World world, Ship fort)
    {
        Target = FindTarget(world, fort);
        fort.Stance = Target is null ? NpcStance.Patrolling : NpcStance.Hunting;
        if (Target is not { } target)
            return;

        // Swing to bear on the target, or hold on a shot already laid, so the gun points down the warning line.
        var laid = LaidShot(world, fort);
        var aimAt = laid?.Target ?? target.Position;
        var bearing = MathF.Atan2(aimAt.Y - fort.Position.Y, aimAt.X - fort.Position.X);
        var maxTurn = TraverseRate * SimConstants.TickDelta;
        fort.Heading = Angles.Wrap(fort.Heading + Math.Clamp(Angles.Delta(fort.Heading, bearing), -maxTurn, maxTurn));

        for (var slot = 0; slot < Ship.AbilitySlotCount; slot++)
        {
            var ability = fort.Abilities[slot];
            if (ability is null || !ability.IsReady)
                continue;
            Vector2? aim = ability.Definition switch
            {
                LongGun => HunterBehavior.LongGunAim(world, fort, target, fort.FortIslandId),
                Mortar => HunterBehavior.MortarAim(fort, target),
                _ => null,
            };
            if (aim is { } point)
                world.TryCastAbility(fort, (AbilitySlot)slot, point);
        }
    }

    private static ShotWarning? LaidShot(World world, Ship fort)
    {
        foreach (var warning in world.Warnings)
        {
            if (warning.ShipId == fort.Id)
                return warning;
        }
        return null;
    }

    /// <summary>How far its longest gun reaches.</summary>
    public static float Reach(Ship fort)
    {
        var reach = 0f;
        foreach (var ability in fort.Abilities)
        {
            reach = MathF.Max(reach, ability?.Definition switch
            {
                LongGun => LongGun.RangeFor(fort),
                Mortar => Mortar.RangeFor(fort),
                _ => 0f,
            });
        }
        return reach;
    }

    private static Ship? FindTarget(World world, Ship fort)
    {
        var reach = Reach(fort);
        Ship? nearest = null;
        var nearestDistance = reach * reach;
        foreach (var other in world.Ships)
        {
            if (other.Team == fort.Team || other.IsSunk)
                continue;
            var d = Vector2.DistanceSquared(other.Position, fort.Position);
            if (d <= nearestDistance)
            {
                nearest = other;
                nearestDistance = d;
            }
        }
        return nearest;
    }
}
