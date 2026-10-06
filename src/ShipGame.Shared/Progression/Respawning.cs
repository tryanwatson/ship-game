using System.Numerics;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Progression;

/// <summary>
/// Sunk players come back after <see cref="DelaySeconds"/>, near a living teammate, keeping their gold, weapons,
/// skills, upgrades, and cards. If every player is down at once there's no one left to come back to: the run is over.
/// </summary>
public static class Respawning
{
    public const float DelaySeconds = 10f;
    public static readonly int DelayTicks = (int)(DelaySeconds * SimConstants.TickRate);

    /// <summary>How far from the teammate to put the returning ship.</summary>
    public const float DistanceFromTeammate = 8f;

    private const float MinDistanceFromLand = 3f;
    private const float MinDistanceFromPirates = 15f;
    private const int Candidates = 16;

    /// <summary>Called for each player ship as it sinks, before it's removed.</summary>
    public static void OnPlayerSunk(World world, Ship ship, PlayerState player)
    {
        player.LostShip = ship;
        player.RespawnTicksRemaining = DelayTicks;
        world.Emit(new PlayerSunk(world.Tick, player.PlayerId, DelayTicks));
    }

    /// <summary>After sinkings: end the run on a wipe, otherwise count down and bring players back.</summary>
    public static void Step(World world)
    {
        if (world.IsRunOver || world.Players.Count == 0)
            return;

        if (!world.Ships.Any(s => s.OwnerPlayerId is not null))
        {
            world.EndRun();
            return;
        }

        foreach (var player in world.Players.Values)
        {
            if (player.RespawnTicksRemaining <= 0 || --player.RespawnTicksRemaining > 0)
                continue;
            Respawn(world, player);
        }
    }

    private static void Respawn(World world, PlayerState player)
    {
        var lost = player.LostShip;
        var stats = lost?.BaseStats ?? ShipStats.Sloop;
        var abilities = lost?.Abilities.Select(a => a?.Definition).ToList();

        var position = PickSpawnPoint(world, player.PlayerId);
        var ship = world.SpawnShip(position, Archipelago.StartHeading, stats, player.PlayerId, abilities);

        // Upgrades, skills, and cards carry over; re-applying them also tops health up to the new maximum. Cards come
        // from the player, since some may have been chosen while they waited.
        if (lost is not null)
        {
            foreach (var modifier in lost.Modifiers.Where(m => !Statuses.IsStatusSource(m.Source))) // buffs and debuffs don't come back with you
                ship.AddModifier(modifier);
            foreach (var skill in lost.Skills)
                ship.AddSkill(skill);
        }
        ship.ReplaceCards(player.Cards);

        player.LostShip = null;
        world.Emit(new PlayerRespawned(world.Tick, player.PlayerId, ship.Id));
    }

    /// <summary>
    /// A clear patch of water beside a living teammate: away from land and pirates. Candidates
    /// are spread evenly around the teammate (no randomness, so the server stays reproducible); the best one wins.
    /// </summary>
    private static Vector2 PickSpawnPoint(World world, int playerId)
    {
        var teammates = world.Ships.Where(s => s.OwnerPlayerId is not null && s.OwnerPlayerId != playerId).ToList();
        if (teammates.Count == 0)
            return Archipelago.Start;

        var anchor = teammates[(int)((world.Tick + playerId) % teammates.Count)].Position;
        var pirates = world.Ships.Where(s => s.Team == Team.Pirates).Select(s => s.Position).ToList();
        var startAngle = (world.Tick + playerId * 7) % Candidates;

        var best = anchor;
        var bestScore = float.MinValue;
        for (var i = 0; i < Candidates; i++)
        {
            var angle = MathF.Tau * ((startAngle + i) % Candidates) / Candidates;
            var candidate = Vector2.Clamp(
                anchor + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * DistanceFromTeammate,
                new Vector2(MinDistanceFromLand), world.WorldSize - new Vector2(MinDistanceFromLand));

            var land = world.DistanceToLand(candidate);
            var pirate = pirates.Count == 0 ? float.MaxValue : pirates.Min(p => Vector2.Distance(p, candidate));
            if (land >= MinDistanceFromLand && pirate >= MinDistanceFromPirates)
                return candidate;

            var score = MathF.Min(land / MinDistanceFromLand, pirate / MinDistanceFromPirates);
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }
}
