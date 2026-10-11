using System.Numerics;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Progression;

/// <summary>
/// Sunk players come back after a delay (<see cref="DelaySeconds"/>, longer each time they sink at the same stop), near
/// a living teammate, at <see cref="ReturnHealth"/> of their hull, keeping their gold, weapons, skills, upgrades, and
/// cards. If every player is down at once there's no one left to come back to: the run is over, unless a lone sailor
/// has a lifeboat left this act (<see cref="PlayerState.ExtraLives"/>), which brings them back at the stop's entry.
/// </summary>
public static class Respawning
{
    public const float DelaySeconds = 10f;
    public static readonly int DelayTicks = (int)(DelaySeconds * SimConstants.TickRate);

    /// <summary>The longest anyone waits, however often they've sunk.</summary>
    public const float MaxDelaySeconds = 40f;

    /// <summary>Of their full hull, what a returning ship comes back with.</summary>
    public const float ReturnHealth = 0.5f;

    /// <summary>Lifeboats a lone sailor gets each act.</summary>
    public const int SoloLivesPerAct = 1;

    /// <summary>The wait for a ship sunk for the <paramref name="deaths"/>th time at this stop (from 1): 10, 20, 30, 40 seconds.</summary>
    public static int DelayTicksFor(int deaths) =>
        (int)(MathF.Min(MaxDelaySeconds, DelaySeconds * Math.Max(1, deaths)) * SimConstants.TickRate);

    /// <summary>How far from the teammate to put the returning ship.</summary>
    public const float DistanceFromTeammate = 8f;

    private const float MinDistanceFromLand = 3f;
    private const float MinDistanceFromPirates = 15f;
    private const int Candidates = 16;

    /// <summary>Called for each player ship as it sinks, before it's removed.</summary>
    public static void OnPlayerSunk(World world, Ship ship, PlayerState player)
    {
        player.LostShip = ship;
        player.DeathsThisStop++;
        player.RespawnTicksRemaining = DelayTicksFor(player.DeathsThisStop);
        // Alone, with a lifeboat to spare: this sinking doesn't end the run.
        if (world.Players.Count == 1 && player.ExtraLives > 0)
        {
            player.ExtraLives--;
            player.OnLifeboat = true;
        }
        world.Emit(new PlayerSunk(world.Tick, player.PlayerId, player.RespawnTicksRemaining));
    }

    /// <summary>After sinkings: end the run on a wipe, otherwise count down and bring players back.</summary>
    public static void Step(World world)
    {
        if (world.IsRunOver || world.Players.Count == 0)
            return;

        if (!world.Ships.Any(s => s.OwnerPlayerId is not null) && !world.Players.Values.Any(p => p.OnLifeboat && p.IsAwaitingRespawn))
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

    /// <summary>A sunk player comes back straight away at <paramref name="position"/>, with the crew sailing into a new region.</summary>
    public static void RespawnNow(World world, PlayerState player, Vector2 position)
    {
        player.RespawnTicksRemaining = 0;
        Respawn(world, player, position);
    }

    private static void Respawn(World world, PlayerState player, Vector2? at = null)
    {
        var lost = player.LostShip;
        var stats = lost?.BaseStats ?? ShipStats.Sloop;
        var abilities = lost?.Abilities.Select(a => a?.Definition).ToList();

        var position = at ?? PickSpawnPoint(world, player.PlayerId);
        var ship = world.SpawnShip(position, Regions.EntryHeading, stats, player.PlayerId, abilities);

        // Upgrades, skills, and cards carry over; re-applying them also tops health up to the new maximum. Cards come
        // from the player, since some may have been chosen while they waited.
        if (lost is not null)
        {
            foreach (var modifier in lost.Modifiers.Where(m => !Statuses.IsStatusSource(m.Source))) // buffs and debuffs don't come back with you
                ship.AddModifier(modifier);
            foreach (var skill in lost.Skills)
                ship.AddSkill(skill);
            foreach (var (tally, value) in lost.Tallies)
                ship.AddToTally(tally, value); // what its cards have grown with
        }
        ship.ReplaceCards(player.Cards);
        ship.Health = ship.Stats.MaxHealth * ReturnHealth;

        player.LostShip = null;
        player.OnLifeboat = false;
        world.Emit(new PlayerRespawned(world.Tick, player.PlayerId, ship.Id));
    }

    /// <summary>
    /// A clear patch of water beside a living teammate (or the entry): away from land and pirates. Candidates
    /// are spread evenly around the teammate (no randomness, so the server stays reproducible); the best one wins.
    /// </summary>
    private static Vector2 PickSpawnPoint(World world, int playerId)
    {
        // Beside a teammate; with nobody afloat (a lone sailor's lifeboat), where the crew sailed in.
        var teammates = world.Ships.Where(s => s.OwnerPlayerId is not null && s.OwnerPlayerId != playerId).ToList();
        var anchor = teammates.Count == 0 ? world.RegionEntry : teammates[(int)((world.Tick + playerId) % teammates.Count)].Position;
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
