using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Trading;

namespace ShipGame.Shared.Progression;

/// <summary>Sets up a run on the <see cref="Archipelago"/>: the same for a solo game and a dedicated server.</summary>
public static class Runs
{
    /// <summary>Tiles between neighbouring ships in the starting line.</summary>
    public const float StartSpacing = 5f;

    /// <summary>
    /// A fresh run: the map's islands and markets, the crew lined up abreast at the southern edge facing north (each
    /// with their chosen weapon), the pirates at sea, and the storm and raids to come.
    /// </summary>
    public static World Create(int seed, IReadOnlyList<(int PlayerId, Ability Weapon)> crew, bool friendlyFire = false)
    {
        var world = CreateMap();
        world.Director = new RunDirector(seed, world.WorldSize);
        world.FriendlyFire = friendlyFire;
        Contracts.OpenMarkets(world, seed);

        for (var i = 0; i < crew.Count; i++)
            world.SpawnShip(StartPosition(i, crew.Count), Archipelago.StartHeading, ShipStats.Sloop, crew[i].PlayerId,
                Loadouts.Starting(crew[i].Weapon));

        PirateCamps.Populate(world, seed: seed);
        return world;
    }

    /// <summary>The map's islands on open water, with nothing else: for menus, mirrors, and tests.</summary>
    public static World CreateMap()
    {
        var world = new World(Archipelago.Size);
        foreach (var island in Archipelago.CreateIslands())
            world.AddIsland(island);
        return world;
    }

    /// <summary>The <paramref name="index"/>th of <paramref name="count"/> ships abreast on the start line.</summary>
    public static Vector2 StartPosition(int index, int count) =>
        Archipelago.Start + new Vector2((index - (count - 1) / 2f) * StartSpacing, 0f);
}
