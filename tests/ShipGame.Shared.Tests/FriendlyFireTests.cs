using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

public class FriendlyFireTests
{
    /// <summary>Player 1 at (30,30) heading east; player 2 four tiles off its starboard beam.</summary>
    private static (World world, Ship shooter, Ship other) TwoPlayers(bool friendlyFire)
    {
        var world = new World(new Vector2(128, 128)) { Wind = Vector2.Zero, FriendlyFire = friendlyFire };
        var shooter = world.SpawnShip(new Vector2(30, 30), 0f, ShipStats.Sloop, 1, Loadouts.Sloop);
        var other = world.SpawnShip(new Vector2(30, 34), 0f, ShipStats.Sloop, 2, Loadouts.Sloop);
        return (world, shooter, other);
    }

    private static void FireStarboardAndWait(World world, Ship shooter)
    {
        world.TryCastAbility(shooter, AbilitySlot.One, shooter.Position + new Vector2(0, 5));
        for (var t = 0; t < SimConstants.TickRate; t++)
            world.Step();
    }

    [Fact]
    public void WithFriendlyFire_PlayersHurtEachOther()
    {
        var (world, shooter, other) = TwoPlayers(friendlyFire: true);

        FireStarboardAndWait(world, shooter);

        Assert.True(other.Health < other.Stats.MaxHealth);
        Assert.Equal(shooter.Stats.MaxHealth, shooter.Health);
        Assert.Equal(shooter.Id, other.LastHitByShipId);
    }

    [Fact]
    public void WithoutFriendlyFire_PlayersAreSafeFromEachOther()
    {
        var (world, shooter, other) = TwoPlayers(friendlyFire: false);

        FireStarboardAndWait(world, shooter);

        Assert.Equal(other.Stats.MaxHealth, other.Health);
    }

    [Fact]
    public void PiratesNeverHurtEachOther_EvenWithFriendlyFire()
    {
        var world = new World(new Vector2(128, 128)) { Wind = Vector2.Zero, FriendlyFire = true };
        var pirate = world.SpawnShip(new Vector2(30, 30), 0f, ShipStats.Sloop, abilities: Loadouts.Sloop);
        var crewmate = world.SpawnShip(new Vector2(30, 34), 0f, ShipStats.Sloop);

        FireStarboardAndWait(world, pirate);

        Assert.Equal(crewmate.Stats.MaxHealth, crewmate.Health);
    }

    [Fact]
    public void NoShipEverHurtsItself()
    {
        var (world, shooter, _) = TwoPlayers(friendlyFire: true);

        Assert.False(world.CanDamage(shooter.Id, shooter.Team, shooter));
    }

    [Fact]
    public void PvpKill_PaysTheKiller()
    {
        var (world, shooter, other) = TwoPlayers(friendlyFire: true);
        other.Health = 1f;
        world.DrainEvents();

        FireStarboardAndWait(world, shooter);

        Assert.DoesNotContain(other, world.Ships);
        Assert.Equal(KillRewards.Gold, world.Players[1].Gold);
        Assert.Equal(1, world.Players[1].Kills);
        Assert.Equal(ShipStats.Sloop.MaxSpeed * (1f + KillRewards.SpeedBonus), shooter.Stats.MaxSpeed, 4);
        var sunk = Assert.Single(world.DrainEvents().OfType<ShipSunk>());
        Assert.Equal(shooter.Id, sunk.KillerShipId);
        Assert.True(world.Players[2].IsAwaitingRespawn); // the victim's teammate (the killer) is still afloat
    }
}
