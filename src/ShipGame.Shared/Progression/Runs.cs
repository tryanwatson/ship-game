using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Shared.Progression;

/// <summary>Sets up a run across a <see cref="SeaChart"/>: the same for a solo game and a dedicated server.</summary>
public static class Runs
{
    /// <summary>Tiles between neighbouring ships in the line they sail into each region in.</summary>
    public const float StartSpacing = 5f;

    /// <summary>
    /// A fresh run: the crew (unarmed, with <paramref name="startingGold"/>) at the start of a newly drawn chart, in
    /// open water, and the director to run the voyage (see <see cref="RunDirector"/>). It opens paused: each player
    /// chooses a free starting card (any card, weapon cards included, rerollable with their gold), then the weapon to
    /// set sail with, and play starts once everyone has; then the crew votes where to sail first. A
    /// <paramref name="testing"/> run deals a late game's worth of hands instead of the one starting card (see
    /// <see cref="CardRewards.OfferTesting"/>), to try out late-game fights without playing up to them.
    /// </summary>
    /// <param name="crew">Who's sailing, and what they're called.</param>
    /// <param name="startingGold">Gold each player starts with: 0 normally, more for playtesting (up to <see cref="MaxStartingGold"/>).</param>
    public static World Create(int seed, IReadOnlyList<(int PlayerId, string Name)> crew, bool friendlyFire = false, int startingGold = 0,
        bool testing = false)
    {
        var world = new World(Regions.StartSize);
        var director = new RunDirector(seed);
        world.Director = director;
        world.FriendlyFire = friendlyFire;

        for (var i = 0; i < crew.Count; i++)
        {
            // Anywhere for now: the start's region lines them up at its entry.
            world.SpawnShip(world.WorldSize / 2f, Regions.EntryHeading, ShipStats.Sloop, crew[i].PlayerId, new Ability?[Ship.AbilitySlotCount]);
            var player = world.GetOrAddPlayer(crew[i].PlayerId);
            player.Name = PlayerNames.Clean(crew[i].Name) is { Length: > 0 } name ? name : PlayerNames.Default;
            player.NeedsStartingWeapon = true;
        }
        startingGold = Math.Clamp(startingGold, 0, MaxStartingGold);
        if (startingGold > 0)
            foreach (var (playerId, _) in crew)
                world.AddGold(playerId, startingGold);

        GrantLifeboats(world);
        director.Begin(world);
        // The starting card: free, dealt at level 1.
        if (testing)
            CardRewards.OfferTesting(world, director.Rng);
        else
            CardRewards.OfferAll(world, director.Rng, OfferSource.Start, 1);
        return world;
    }

    /// <summary>
    /// The weapon a player sets sail with, chosen once their starting card is: it goes on slot 1, and its starting bonus
    /// on the ship (it carries over a respawn like an upgrade). Null on success.
    /// </summary>
    public static RejectionReason? TryChooseStartingWeapon(World world, int playerId, string abilityId)
    {
        if (!world.Players.TryGetValue(playerId, out var player) || !player.NeedsStartingWeapon)
            return RejectionReason.NotChoosingWeapon;
        if (player.CardOffers.Count > 0)
            return RejectionReason.ChooseCardFirst;
        if (WeaponCatalog.Find(abilityId) is not { } weapon)
            return RejectionReason.UnknownWeapon;
        if (world.GetPlayerShip(playerId) is not { } ship)
            return RejectionReason.NoShip;

        ship.SetAbility(AbilitySlot.One, weapon.Ability);
        foreach (var modifier in weapon.StartingBonus)
            ship.AddModifier(modifier);
        player.NeedsStartingWeapon = false;
        world.Emit(new StartingWeaponChosen(world.Tick, playerId, weapon.Id));
        world.Emit(new AbilityUnlocked(world.Tick, ship.Id, weapon.Id, AbilitySlot.One));
        return null;
    }

    /// <summary>
    /// A lone sailor gets <see cref="Respawning.SoloLivesPerAct"/> lifeboats, at the start of the run and of every act
    /// after (unused ones don't pile up). Crews don't: they have each other.
    /// </summary>
    public static void GrantLifeboats(World world)
    {
        if (world.Players.Count != 1)
            return;
        foreach (var player in world.Players.Values)
            player.ExtraLives = Math.Max(player.ExtraLives, Respawning.SoloLivesPerAct);
    }

    /// <summary>The most gold a run can be set to start with (a playtesting option).</summary>
    public const int MaxStartingGold = 99_999;

    /// <summary>The old hand-placed archipelago on open water, with nothing else: the sea behind the title menu, and for tests.</summary>
    public static World CreateMap()
    {
        var world = new World(Archipelago.Size);
        foreach (var island in Archipelago.CreateIslands())
            world.AddIsland(island);
        return world;
    }
}
